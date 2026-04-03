using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GTasksBar
{
    public class TaskItem : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public bool IsNew { get; set; } // Tells the UI to auto-focus when created

        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private string _details;
        public string Details
        {
            get => _details;
            set { _details = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDetails)); }
        }

        // Checks if there is text in the details field
        public bool HasDetails => !string.IsNullOrEmpty(Details);

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}