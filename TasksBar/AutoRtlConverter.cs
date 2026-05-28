using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace TasksBar
{
    public class AutoRtlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                // THE FIX: Check if ANY character in the entire string is in the Hebrew/Arabic Unicode block
                bool containsHebrew = text.Any(c => c >= 0x0590 && c <= 0x06FF);

                if (containsHebrew)
                {
                    return FlowDirection.RightToLeft;
                }
            }

            // Default to English LTR if empty or contains absolutely no Hebrew
            return FlowDirection.LeftToRight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}