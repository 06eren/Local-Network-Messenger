using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace Local_Network_Messenger.UI
{
    public static class PasswordBoxAssistant
    {
        public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false, OnBindPasswordChanged));

        public static readonly DependencyProperty SecurePasswordProperty = DependencyProperty.RegisterAttached(
            "SecurePassword",
            typeof(SecureString),
            typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSecurePasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false));

        public static bool GetBindPassword(DependencyObject dependencyObject)
        {
            return (bool)dependencyObject.GetValue(BindPasswordProperty);
        }

        public static void SetBindPassword(DependencyObject dependencyObject, bool value)
        {
            dependencyObject.SetValue(BindPasswordProperty, value);
        }

        public static SecureString? GetSecurePassword(DependencyObject dependencyObject)
        {
            return (SecureString?)dependencyObject.GetValue(SecurePasswordProperty);
        }

        public static void SetSecurePassword(DependencyObject dependencyObject, SecureString? value)
        {
            dependencyObject.SetValue(SecurePasswordProperty, value);
        }

        private static bool GetIsUpdating(DependencyObject dependencyObject)
        {
            return (bool)dependencyObject.GetValue(IsUpdatingProperty);
        }

        private static void SetIsUpdating(DependencyObject dependencyObject, bool value)
        {
            dependencyObject.SetValue(IsUpdatingProperty, value);
        }

        private static void OnBindPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
        {
            if (dependencyObject is not PasswordBox passwordBox)
            {
                return;
            }

            if (eventArgs.OldValue is bool wasBound && wasBound)
            {
                passwordBox.PasswordChanged -= HandlePasswordChanged;
            }

            if (eventArgs.NewValue is bool shouldBind && shouldBind)
            {
                passwordBox.PasswordChanged += HandlePasswordChanged;
            }
        }

        private static void OnSecurePasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
        {
            if (dependencyObject is not PasswordBox passwordBox)
            {
                return;
            }

            if (GetIsUpdating(passwordBox))
            {
                return;
            }

            if (eventArgs.NewValue is not SecureString securePassword || securePassword.Length == 0)
            {
                passwordBox.Clear();
            }
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not PasswordBox passwordBox)
            {
                return;
            }

            SetIsUpdating(passwordBox, true);
            SetSecurePassword(passwordBox, passwordBox.SecurePassword.Copy());
            SetIsUpdating(passwordBox, false);
        }
    }
}
