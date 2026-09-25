#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Graphics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using DocumentationOfWorkingHours.Services;

namespace DocumentationOfWorkingHours.ViewModels
{
    public class DayDisplay : BindableObject
    {
        int? _dayNumber;
        string? _note;
        DateTime? _date;
        bool _isToday;

        public int? DayNumber { get => _dayNumber; set { _dayNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlaceholder)); } }
        public string? Note { get => _note; set { _note = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayNote)); } }
        public bool IsPlaceholder => DayNumber == null;
        public bool IsToday { get => _isToday; set { _isToday = value; OnPropertyChanged(); } }
        public DateTime? Date { get => _date; set { _date = value; OnPropertyChanged(); } }

        // Return an empty string when note is null/empty or numeric zero
        public string DisplayNote
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Note)) return string.Empty;
                if (double.TryParse(Note, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var v))
                {
                    if (Math.Abs(v) < 0.0001) return string.Empty;
                }
                return Note ?? string.Empty;
            }
        }
    }

    public class CalendarPageViewModel : BindableObject
    {
        public ObservableCollection<DayDisplay> Days { get; } = new();
        public ObservableCollection<WeekSummary> WeekSummaries { get; } = new();

        DayDisplay? _selectedDay;
        public DayDisplay? SelectedDay
        {
            get => _selectedDay;
            set
            {
                // Sichtbarkeit nur aktivieren, wenn eine andere (oder von null kommende) Auswahl getroffen wurde.
                if (value == null)
                {
                    _selectedDay = null;
                    OnPropertyChanged();
                    IsEditing = false;
                    return;
                }

                // If the selected item is a placeholder (no day number), do not allow editing
                // Revert the selection so placeholders are not selectable in the UI.
                if (value.DayNumber == null)
                {
                    // Do not set _selectedDay to the placeholder. Notify binding so UI resets to previous selection (or clears selection).
                    OnPropertyChanged(nameof(SelectedDay));
                    IsEditing = false;
                    return;
                }

                var wasNull = _selectedDay == null;
                var sameDate = !wasNull && _selectedDay?.Date?.Date == value.Date?.Date;

                _selectedDay = value;
                OnPropertyChanged();

                // Notify SelectedHours binding when selection changes
                OnPropertyChanged(nameof(SelectedHours));

                // Wenn vorher keine Auswahl war oder ein anderer Tag gewählt wurde -> Editing an, sonst aus
                IsEditing = wasNull || !sameDate;
            }
        }

        bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        private DateTime _current;
        public string MonthTitle => _current.ToString("Y", CultureInfo.CurrentCulture);

        // Text for week sums shown in the UI (e.g. "W1: 20h / W2: 40h")
        string _weekSumsText = string.Empty;
        public string WeekSumsText
        {
            get => _weekSumsText;
            set { _weekSumsText = value; OnPropertyChanged(); }
        }

        public ICommand PrevMonthCommand { get; }
        public ICommand NextMonthCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SelectDayCommand { get; }

        public CalendarPageViewModel()
        {
            _current = DateTime.Today;
            PrevMonthCommand = new Command(async () => await ChangeMonthAsync(-1));
            NextMonthCommand = new Command(async () => await ChangeMonthAsync(1));
            SaveCommand = new Command(async () => await SaveNoteAsync());
            SelectDayCommand = new Command<DayDisplay>(d => OnSelectDay(d));
            _ = LoadMonthAsync(_current);
        }

        void OnSelectDay(DayDisplay? d)
        {
            if (d == null) return;
            // If the same day is already selected, clear selection first so SelectedItem binding changes
            if (SelectedDay != null && SelectedDay.Date?.Date == d.Date?.Date)
            {
                SelectedDay = null;
            }
            // Now set the selection to trigger the setter logic and open editor
            SelectedDay = d;
        }

        private async Task ChangeMonthAsync(int offset)
        {
            _current = _current.AddMonths(offset);
            OnPropertyChanged(nameof(MonthTitle));
            await LoadMonthAsync(_current);
        }

        private Task LoadMonthAsync(DateTime month)
        {
            Days.Clear();

            var first = new DateTime(month.Year, month.Month, 1);
            // In many cultures week starts on Monday; adjust so Monday=1..Sunday=7
            var dayOfWeek = ((int)first.DayOfWeek + 6) % 7; // 0 = Monday
            for (int i = 0; i < dayOfWeek; i++)
            {
                Days.Add(new DayDisplay { DayNumber = null });
            }

            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            for (int d = 1; d <= daysInMonth; d++)
            {
                var dt = new DateTime(month.Year, month.Month, d);
                // Try to load notes from persisted Calendar (if available)
                string? note = null;
                try
                {
                    var path = CalendarStore.GetDefaultPath();
                    var cal = CalendarStore.LoadAsync(path).GetAwaiter().GetResult();
                    if (cal != null)
                    {
                        // naive search: find a matching Day by Date
                        foreach (var m in cal.Months)
                        {
                            foreach (var w in m.Weeks)
                            {
                                foreach (var day in w.Days)
                                {
                                    if (day.Date.Date == dt.Date)
                                    {
                                        note = day.Notes;
                                        goto found;
                                    }
                                }


                            }
                        }
                    }
                }
                catch { }
                found:
                Days.Add(new DayDisplay { DayNumber = d, Date = dt, Note = note, IsToday = dt.Date == DateTime.Today });
            }

            // Fill trailing placeholders to complete the last week
            while (Days.Count % 7 != 0)
                Days.Add(new DayDisplay { DayNumber = null });

            // Recalculate week sums for the loaded month
            RecalcWeekSums();

            return Task.CompletedTask;
        }

        private async Task SaveNoteAsync()
        {
            if (SelectedDay?.Date == null) return;

            try
            {
                var path = CalendarStore.GetDefaultPath();
                var cal = await CalendarStore.LoadAsync(path) ?? new DocumentationOfWorkingHours.Services.Calendar();

                // Try to find existing Day and update
                bool updated = false;
                foreach (var m in cal.Months)
                {
                    foreach (var w in m.Weeks)
                    {
                        foreach (var day in w.Days)
                        {
                            if (day.Date.Date == SelectedDay.Date.Value.Date)
                            {
                                day.Notes = SelectedDay.Note;
                                updated = true;
                                break;
                            }
                        }
                        if (updated) break;
                    }
                    if (updated) break;
                }

                if (!updated)
                {
                    // Create month key as yyyy-MM to avoid human readable collisions
                    var monthKey = SelectedDay.Date.Value.ToString("yyyy-MM");
                    var month = cal.Months.Find(m => m.Name == monthKey);
                    if (month == null)
                    {
                        month = new Month(monthKey);
                        cal.Months.Add(month);
                    }
                    // Add a simple week container with the single day
                    var week = new Week(new[] { new Day(SelectedDay.Date.Value, SelectedDay.Note) });
                    month.Weeks.Add(week);
                }

                await CalendarStore.SaveAsync(cal, path);

                // Ensure UI shows the updated note immediately
                var selDate = SelectedDay.Date.Value.Date;
                var existing = System.Linq.Enumerable.FirstOrDefault(Days, d => d.Date?.Date == selDate);
                if (existing != null)
                {
                    existing.Note = SelectedDay.Note;
                }

                // Notify selection changed to refresh bindings if needed
                OnPropertyChanged(nameof(SelectedDay));

                // Nach dem Speichern Eingabemodus beenden, Auswahl beibehalten
                IsEditing = false;
                // Recalculate week sums after saving note
                RecalcWeekSums();
            }
            catch { }
        }

        void RecalcWeekSums()
        {
            try
            {
                var cal = CultureInfo.CurrentCulture.Calendar;
                var rule = CalendarWeekRule.FirstFourDayWeek;
                var firstDay = DayOfWeek.Monday;

                var groups = Days
                    .Where(d => d.Date != null)
                    .GroupBy(d => cal.GetWeekOfYear(d.Date!.Value, rule, firstDay))
                    .Select(g => new
                    {
                        Week = g.Key,
                        Sum = g.Sum(x =>
                        {
                            if (string.IsNullOrWhiteSpace(x.Note)) return 0.0;
                            // try parse note as a numeric hours value
                            if (double.TryParse(x.Note, System.Globalization.NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
                                return v;
                            // fallback: try to extract leading number token
                            var firstToken = x.Note.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (firstToken != null && double.TryParse(firstToken, System.Globalization.NumberStyles.Float, CultureInfo.CurrentCulture, out var v2))
                                return v2;
                            return 0.0;
                        })
                    })
                    .OrderBy(g => g.Week)
                    .ToList();
                WeekSumsText = string.Join(" / ", groups.Select(g => $"W{g.Week}: {g.Sum}h"));

                // Populate WeekSummaries collection so UI can color weeks >= 40h
                WeekSummaries.Clear();
                foreach (var g in groups)
                {
                    var ws = new WeekSummary
                    {
                        WeekNumber = g.Week,
                        SumHours = g.Sum,
                        Label = $"W{g.Week}: {g.Sum}h",
                        TextColor = g.Sum >= 40.0 ? Colors.Green : (Color)Colors.Gray
                    };
                    WeekSummaries.Add(ws);
                }
                OnPropertyChanged(nameof(WeekSummaries));
            }
            catch
            {
                WeekSumsText = string.Empty;
            }
        }

        // Helper property for binding to a Stepper control. Reads/writes SelectedDay.Note as numeric hours.
        public double SelectedHours
        {
            get
            {
                if (SelectedDay?.Note != null && double.TryParse(SelectedDay.Note, System.Globalization.NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
                    return v;
                return 0.0;
            }
            set
            {
                if (SelectedDay == null) return;
                var s = value.ToString(CultureInfo.CurrentCulture);
                if (SelectedDay.Note != s)
                {
                    SelectedDay.Note = s;
                    OnPropertyChanged(nameof(SelectedHours));
                    OnPropertyChanged(nameof(SelectedHoursDisplay));
                    OnPropertyChanged(nameof(SelectedDay));
                    // update persistent storage if desired; also update sums immediately
                    RecalcWeekSums();
                }
            }
        }

        public string SelectedHoursDisplay => Math.Abs(SelectedHours) < 0.0001 ? string.Empty : SelectedHours.ToString("N1", CultureInfo.CurrentCulture) + "h";
    }

    public class WeekSummary
    {
        public int WeekNumber { get; set; }
        public double SumHours { get; set; }
        public string Label { get; set; } = string.Empty;
        public Color TextColor { get; set; } = Colors.Gray;
    }

}
