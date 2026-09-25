using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using DocumentationOfWorkingHours.ViewModels;

namespace DocumentationOfWorkingHours.Views
{
    public partial class CalendarPage : ContentPage
    {
        const double MinValue = 1.0;
        const double MaxValue = 10.0;
        public CalendarPage()
        {
            InitializeComponent();
            BindingContext = new CalendarPageViewModel();
        }

        void OnMinusClicked(object sender, EventArgs e)
        {
            if (BindingContext is CalendarPageViewModel vm)
            {
                var val = vm.SelectedHours;
                val = Math.Max(0, val - 0.5);
                vm.SelectedHours = val;
            }
        }

        void OnPlusClicked(object sender, EventArgs e)
        {
            if (BindingContext is CalendarPageViewModel vm)
            {
                var val = vm.SelectedHours;
                val = Math.Min(10, val + 0.5);
                vm.SelectedHours = val;
            }
        }

        void OnEntryCompleted(object sender, EventArgs e)
        {
            if (BindingContext is CalendarPageViewModel vm && vm.SaveCommand != null)
            {
                if (vm.SaveCommand.CanExecute(null))
                    vm.SaveCommand.Execute(null);
            }
        }

        void OnHoursEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                var text = entry.Text ?? string.Empty;
                var sb = new System.Text.StringBuilder();

                // Use current culture decimal separator as preferred symbol
                var cultureSep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
                bool seenDecimal = false;

                foreach (var ch in text)
                {
                    // allow only digits
                    if (char.IsDigit(ch))
                    {
                        sb.Append(ch);
                        continue;
                    }

                    // allow one decimal separator (either '.' or ',' or culture sep)
                    if (!seenDecimal && (ch == '.' || ch == ',' || ch == cultureSep))
                    {
                        // append culture separator for consistency
                        sb.Append(cultureSep);
                        seenDecimal = true;
                    }

                    // ignore everything else (including '-') => only positive numbers allowed
                }

                var filtered = sb.ToString();

                // If parseable, enforce min/max bounds
                if (double.TryParse(filtered, NumberStyles.Float, CultureInfo.CurrentCulture, out var val))
                {
                    if (val < MinValue) val = MinValue;
                    else if (val > MaxValue) val = MaxValue;
                    var clamped = val.ToString(CultureInfo.CurrentCulture);
                    if (clamped != filtered)
                    {
                        filtered = clamped;
                    }
                }

                if (filtered != text)
                {
                    // attempt to preserve cursor position
                    var pos = Math.Min(filtered.Length, Math.Max(0, entry.CursorPosition));
                    entry.Text = filtered;
                    try { entry.CursorPosition = pos; } catch { }
                }
            }
        }
    }
}
