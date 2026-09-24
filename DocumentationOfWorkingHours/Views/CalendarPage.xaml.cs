using System;
using Microsoft.Maui.Controls;
using DocumentationOfWorkingHours.ViewModels;

namespace DocumentationOfWorkingHours.Views
{
    public partial class CalendarPage : ContentPage
    {
        public CalendarPage()
        {
            InitializeComponent();
            BindingContext = new CalendarPageViewModel();
        }

        void OnHoursEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                var text = entry.Text ?? string.Empty;
                var sb = new System.Text.StringBuilder();

                // Use current culture decimal separator as preferred symbol
                var cultureSep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
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
