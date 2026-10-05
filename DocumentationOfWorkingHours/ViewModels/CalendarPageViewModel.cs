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
using Microsoft.Maui.ApplicationModel;
using System.Timers;
using DocumentationOfWorkingHours.Services;

namespace DocumentationOfWorkingHours.ViewModels
{
    public class DayDisplay : BindableObject
    {
        int? _dayNumber;
        string? _note;
        DateTime? _date;
        bool _isToday;
        bool _isOtherMonth;

        public int? DayNumber { get => _dayNumber; set { _dayNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlaceholder)); } }
        public string? Note { get => _note; set { _note = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayNote)); } }
        public bool IsPlaceholder => DayNumber == null;
        public bool IsToday { get => _isToday; set { _isToday = value; OnPropertyChanged(); } }
        // True when this cell belongs to an adjacent month (leading/trailing days)
        public bool IsOtherMonth { get => _isOtherMonth; set { _isOtherMonth = value; OnPropertyChanged(); } }
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

                // If the selected item belongs to an adjacent month, do not allow editing
                // Revert the selection so placeholder-adjacent-month cells are not selectable in the UI.
                if (value.IsOtherMonth)
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

                // Notify SelectedHours binding and its display when selection changes
                OnPropertyChanged(nameof(SelectedHours));
                OnPropertyChanged(nameof(SelectedHoursDisplay));

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
        // Simple timer UI bindings and commands
        public ICommand TimerStartCommand { get; private set; }
        public ICommand TimerStopCommand { get; private set; }
        public ICommand TimerResetCommand { get; private set; }

        string _timerDisplay = "00:00:00";
        public string TimerDisplay { get => _timerDisplay; set { _timerDisplay = value; OnPropertyChanged(); } }
        private TimeSpan _timerElapsed = TimeSpan.Zero;
        private System.Timers.Timer? _timer;
        bool _timerIsRunning = false;

        public CalendarPageViewModel()
        {
            _current = DateTime.Today;
            PrevMonthCommand = new Command(async () => await ChangeMonthAsync(-1));
            NextMonthCommand = new Command(async () => await ChangeMonthAsync(1));
            SaveCommand = new Command(async () => await SaveNoteAsync());
            SelectDayCommand = new Command<DayDisplay>(d => OnSelectDay(d));
            TimerStartCommand = new Command(() => StartTimer());
            TimerStopCommand = new Command(() => StopTimer());
            TimerResetCommand = new Command(() => ResetTimer());
            _ = LoadMonthAsync(_current);
        }

        public bool TimerIsRunning { get => _timerIsRunning; private set { _timerIsRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimerStartButtonText)); } }

        public string TimerStartButtonText => !TimerIsRunning && _timerElapsed > TimeSpan.Zero ? "Continue" : "Start";

        void StartTimer()
        {
            if (_timer == null)
            {
                _timer = new System.Timers.Timer(1000) { AutoReset = true };
                _timer.Elapsed += (s, e) =>
                {
                    _timerElapsed = _timerElapsed.Add(TimeSpan.FromSeconds(1));
                    try
                    {
                        MainThread.BeginInvokeOnMainThread(() => TimerDisplay = _timerElapsed.ToString(@"hh\:mm\:ss"));
                    }
                    catch
                    {
                        // ignore
                    }
                };
            }
            _timer?.Start();
            TimerIsRunning = true;
        }

        void StopTimer()
        {
            _timer?.Stop();
            TimerIsRunning = false;
        }

        void ResetTimer()
        {
            StopTimer();
            _timerElapsed = TimeSpan.Zero;
            TimerDisplay = _timerElapsed.ToString(@"hh\:mm\:ss");
            OnPropertyChanged(nameof(TimerStartButtonText));
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
            // Leading placeholders: show the actual day number from the previous month and mark as IsOtherMonth
            for (int i = 0; i < dayOfWeek; i++)
            {
                var placeholderDate = first.AddDays(i - dayOfWeek);
                string? note = null;
                try
                {
                    var path = CalendarStore.GetDefaultPath();
                    var cal = CalendarStore.LoadAsync(path).GetAwaiter().GetResult();
                    if (cal != null)
                    {
                        foreach (var m in cal.Months)
                        {
                            foreach (var w in m.Weeks)
                            {
                                foreach (var day in w.Days)
                                {
                                    if (day.Date.Date == placeholderDate.Date)
                                    {
                                        note = day.Notes;
                                        goto foundLead;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                foundLead:
                Days.Add(new DayDisplay { DayNumber = placeholderDate.Day, Date = placeholderDate, Note = note, IsToday = placeholderDate.Date == DateTime.Today, IsOtherMonth = true });
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
                Days.Add(new DayDisplay { DayNumber = d, Date = dt, Note = note, IsToday = dt.Date == DateTime.Today, IsOtherMonth = false });
            }

            // Fill trailing placeholders to complete the last week
            var last = new DateTime(month.Year, month.Month, daysInMonth);
            var trailingDay = last.AddDays(1);
            while (Days.Count % 7 != 0)
            {
                string? note = null;
                try
                {
                    var path = CalendarStore.GetDefaultPath();
                    var cal = CalendarStore.LoadAsync(path).GetAwaiter().GetResult();
                    if (cal != null)
                    {
                        foreach (var m in cal.Months)
                        {
                            foreach (var w in m.Weeks)
                            {
                                foreach (var day in w.Days)
                                {
                                    if (day.Date.Date == trailingDay.Date)
                                    {
                                        note = day.Notes;
                                        goto foundTrail;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                foundTrail:
                Days.Add(new DayDisplay { DayNumber = trailingDay.Day, Date = trailingDay, Note = note, IsToday = trailingDay.Date == DateTime.Today, IsOtherMonth = true });
                trailingDay = trailingDay.AddDays(1);
            }

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

                // Determine the full date range shown in the calendar grid (including leading/trailing placeholders)
                var firstOfMonth = new DateTime(_current.Year, _current.Month, 1);
                var leadPlaceholders = ((int)firstOfMonth.DayOfWeek + 6) % 7; // 0 = Monday
                var startDisplay = firstOfMonth.AddDays(-leadPlaceholders).Date;

                var lastOfMonth = new DateTime(_current.Year, _current.Month, DateTime.DaysInMonth(_current.Year, _current.Month));
                var tailPlaceholders = ((int)lastOfMonth.DayOfWeek + 6) % 7;
                var endDisplay = lastOfMonth.AddDays(6 - tailPlaceholders).Date;

                // Load notes from persistent store for the displayed range so weeks that span months include
                // notes from adjacent months. Build a date->note map.
                var notesByDate = new Dictionary<DateTime, string?>();
                try
                {
                    var path = CalendarStore.GetDefaultPath();
                    var store = CalendarStore.LoadAsync(path).GetAwaiter().GetResult();
                    if (store != null)
                    {
                        foreach (var m in store.Months)
                        {
                            foreach (var w in m.Weeks)
                            {
                                foreach (var day in w.Days)
                                {
                                    var dDate = day.Date.Date;
                                    if (dDate >= startDisplay && dDate <= endDisplay)
                                    {
                                        notesByDate[dDate] = day.Notes;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // Override with current in-memory Days (unsaved edits should take precedence)
                foreach (var d in Days.Where(d => d.Date != null))
                {
                    notesByDate[d.Date!.Value.Date] = d.Note;
                }

                // Group by calendar week number and sum parsed hours
                var groups = notesByDate
                    .GroupBy(kv => cal.GetWeekOfYear(kv.Key, rule, firstDay))
                    .Select(g => new
                    {
                        Week = g.Key,
                        Sum = g.Sum(kv =>
                        {
                            var s = kv.Value;
                            if (string.IsNullOrWhiteSpace(s)) return 0.0;
                            if (double.TryParse(s, System.Globalization.NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
                                return v;
                            var firstToken = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (firstToken != null && double.TryParse(firstToken, System.Globalization.NumberStyles.Float, CultureInfo.CurrentCulture, out var v2))
                                return v2;
                            return 0.0;
                        })
                    })
                    .OrderBy(g => g.Week)
                    .ToList();

                WeekSumsText = string.Join(" / ", groups.Select(g => $"W{g.Week}: {g.Sum.ToString("N1", CultureInfo.CurrentCulture)}h"));

                // Update total hours for the displayed month (sum of all weeks shown)
                TotalHours = groups.Sum(g => g.Sum);
                OnPropertyChanged(nameof(TotalHoursDisplay));

                // Populate WeekSummaries collection so UI can color weeks >= 25h
                // Update existing items in-place and move/insert to keep object identity.
                var existing = WeekSummaries.ToDictionary(w => w.WeekNumber);
                var index = 0;
                var seenWeeks = new HashSet<int>();

                foreach (var g in groups)
                {
                    var progress = Math.Min(g.Sum / 25.0, 1.0);
                    var isFull = progress >= 1.0;
                    WeekSummary ws;
                    if (existing.TryGetValue(g.Week, out var existingWs))
                    {
                        // update in-place
                        existingWs.SumHours = g.Sum;
                        existingWs.Label = $"W{g.Week}: {g.Sum.ToString("N1", CultureInfo.CurrentCulture)}h";
                        existingWs.Progress = progress;
                        existingWs.ProgressColor = isFull ? Colors.Green : Color.FromArgb("#ac99ea");
                        ws = existingWs;
                    }
                    else
                    {
                        ws = new WeekSummary
                        {
                            WeekNumber = g.Week,
                            SumHours = g.Sum,
                            Label = $"W{g.Week}: {g.Sum.ToString("N1", CultureInfo.CurrentCulture)}h",
                            TextColor = Colors.Gray,
                            Progress = progress,
                            ProgressColor = isFull ? Colors.Green : Color.FromArgb("#ac99ea")
                        };
                    }

                    var currentIndex = WeekSummaries.IndexOf(ws);
                    if (currentIndex == -1)
                    {
                        WeekSummaries.Insert(index, ws);
                    }
                    else if (currentIndex != index)
                    {
                        WeekSummaries.Move(currentIndex, index);
                    }

                    seenWeeks.Add(ws.WeekNumber);
                    index++;
                }

                // Remove stale week summaries that are no longer present.
                for (int i = WeekSummaries.Count - 1; i >= 0; i--)
                {
                    if (!seenWeeks.Contains(WeekSummaries[i].WeekNumber))
                        WeekSummaries.RemoveAt(i);
                }
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

        double _totalHours = 0.0;
        public double TotalHours { get => _totalHours; set { _totalHours = value; OnPropertyChanged(); } }
        public string TotalHoursDisplay => TotalHours.ToString("N1", CultureInfo.CurrentCulture) + "h";
    }

    public class WeekSummary : BindableObject
    {
        int _weekNumber;
        double _sumHours;
        string _label = string.Empty;
        Color _textColor = Colors.Gray;
        double _progress;
        Color _progressColor = Colors.Green;

        public int WeekNumber { get => _weekNumber; set { _weekNumber = value; OnPropertyChanged(); } }
        public double SumHours { get => _sumHours; set { _sumHours = value; OnPropertyChanged(); } }
        public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
        public Color TextColor { get => _textColor; set { _textColor = value; OnPropertyChanged(); } }
        // Progress 0..1 where 1 means 25h or more
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        public Color ProgressColor { get => _progressColor; set { _progressColor = value; OnPropertyChanged(); } }
    }

}
