using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AutoStreamRec
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(
            Func<T> getter,
            Action<T> setter,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (Equals(getter(), value))
                return false;

            void applyChange()
            {
                setter(value);
                OnPropertyChanged(propertyName);
            }

            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.Invoke(applyChange);
            else
                applyChange();

            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}