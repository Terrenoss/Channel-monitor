using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Strivea.ViewModels
{
    public class ViewLocator : IDataTemplate
    {
        public Control Build(object data)
        {
            var name = data.GetType().FullName.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type);
            }

            return new TextBlock { Text = $"Vue non trouvée : {name}" };
        }

        public bool Match(object data)
        {
            return data is ViewModelBase;
        }
    }
} 