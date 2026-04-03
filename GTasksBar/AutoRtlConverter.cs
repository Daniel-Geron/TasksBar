using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace GTasksBar
{
    public class AutoRtlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                // Find the very first actual letter in the text
                var firstLetter = text.FirstOrDefault(char.IsLetter);

                // Check if the character is in the Hebrew or Arabic Unicode blocks
                if (firstLetter >= 0x0590 && firstLetter <= 0x06FF)
                {
                    return FlowDirection.RightToLeft;
                }
            }
            // Default to English LTR if empty or starts with English
            return FlowDirection.LeftToRight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}