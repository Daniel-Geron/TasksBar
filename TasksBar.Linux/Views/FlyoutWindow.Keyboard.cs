using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TasksBar.Linux.Views
{
    // Keyboard behavior of the task list (port of the Windows app's)
    public partial class FlyoutWindow
    {
        private void TasksList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Source is not TextBox box || box.DataContext is not TaskItem task) return;

            // Enter in a title saves it and starts the next task
            if (e.Key == Key.Enter && box.Name == "TaskTitleInput" && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                ClearFocus();
                Dispatcher.UIThread.Post(AddTask, DispatcherPriority.Background);
                return;
            }

            if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None) NavigateVertically(box, task, e);
        }

        // Up/Down walk through the text like one long document: once the caret is on the last line,
        // Down goes title → details → next task's title; Up goes the other way.
        private void NavigateVertically(TextBox box, TaskItem task, KeyEventArgs e)
        {
            bool isTitle = box.Name == "TaskTitleInput";
            if (!isTitle && box.Name != "TaskDetailsInput") return;

            bool down = e.Key == Key.Down;
            var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            var layout = presenter?.TextLayout;
            if (presenter != null && layout != null)
            {
                int line = layout.GetLineIndexFromCharacterIndex(box.CaretIndex, false);
                int lines = layout.TextLines.Count;
                if (down ? line < lines - 1 : line > 0) return; // Still room to move inside this box
            }

            int index = MyTasks.IndexOf(task);
            TaskItem? target;
            bool toDetails;
            if (down)
            {
                target = isTitle ? task : (index + 1 < MyTasks.Count ? MyTasks[index + 1] : null);
                toDetails = isTitle;
            }
            else
            {
                target = !isTitle ? task : (index > 0 ? MyTasks[index - 1] : null);
                toDetails = isTitle;
            }
            if (target == null) return;

            // Keep the caret's horizontal position, like moving between lines of a text editor
            double windowX = double.NaN;
            if (presenter?.TextLayout is { } caretLayout)
            {
                var caret = caretLayout.HitTestTextPosition(box.CaretIndex);
                windowX = presenter.TranslatePoint(new Point(caret.X, 0), this)?.X ?? double.NaN;
            }

            e.Handled = true;
            FocusTaskField(target, toDetails, windowX, enterFromBelow: !down);
        }

        private void FocusTaskField(TaskItem task, bool details, double windowX, bool enterFromBelow)
        {
            if (TasksList.ContainerFromItem(task) is not Control container) return;
            container.BringIntoView();

            var boxes = container.GetVisualDescendants().OfType<TextBox>().ToList();
            var title = boxes.FirstOrDefault(b => b.Name == "TaskTitleInput");
            var detailsBox = boxes.FirstOrDefault(b => b.Name == "TaskDetailsInput");
            if (title == null) return;

            // An empty details box only appears while its task is being edited, so focus the task first
            if (details && detailsBox != null && !detailsBox.IsVisible)
            {
                title.Focus();
                container.UpdateLayout();
            }

            var box = details && detailsBox != null && detailsBox.IsVisible ? detailsBox : title;
            box.Focus();
            box.UpdateLayout();

            int length = box.Text?.Length ?? 0;
            int caretIndex = enterFromBelow ? length : 0;
            var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            if (!double.IsNaN(windowX) && length > 0 && presenter?.TextLayout is { } layout)
            {
                // Land on the first line coming from above, the last line coming from below, at the same x
                var lineRect = layout.HitTestTextPosition(enterFromBelow ? length : 0);
                double x = this.TranslatePoint(new Point(windowX, 0), presenter)?.X ?? 0;
                var hit = layout.HitTestPoint(new Point(x, lineRect.Y + lineRect.Height / 2));
                caretIndex = hit.TextPosition;
            }
            box.CaretIndex = Math.Clamp(caretIndex, 0, length);
            box.SelectionStart = box.SelectionEnd = box.CaretIndex;
        }
    }
}
