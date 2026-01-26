using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Input;

namespace Local_Network_Messenger.ViewModels
{
    public sealed class AuthViewModel : ObservableObject
    {
        private string _username = string.Empty;
        private string _displayName = string.Empty;
        private SecureString? _loginPassword;
        private SecureString? _registerPassword;
        private SecureString? _registerPasswordConfirm;
        private bool _isRegisterMode;
        private string _statusMessage = string.Empty;
        private bool _isBusy;

        public AuthViewModel()
        {
            LoginCommand = new RelayCommand(HandleLogin, CanLogin);
            RegisterCommand = new RelayCommand(HandleRegister, CanRegister);
        }

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (SetProperty(ref _displayName, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public SecureString? LoginPassword
        {
            get => _loginPassword;
            set
            {
                if (SetProperty(ref _loginPassword, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public SecureString? RegisterPassword
        {
            get => _registerPassword;
            set
            {
                if (SetProperty(ref _registerPassword, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public SecureString? RegisterPasswordConfirm
        {
            get => _registerPasswordConfirm;
            set
            {
                if (SetProperty(ref _registerPasswordConfirm, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool IsRegisterMode
        {
            get => _isRegisterMode;
            set
            {
                if (SetProperty(ref _isRegisterMode, value))
                {
                    OnPropertyChanged(nameof(IsLoginMode));
                    StatusMessage = string.Empty;
                    ClearSensitiveFields();
                    UpdateCommandStates();
                }
            }
        }

        public bool IsLoginMode
        {
            get => !IsRegisterMode;
            set
            {
                if (value == IsLoginMode)
                {
                    return;
                }

                IsRegisterMode = !value;
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public ICommand LoginCommand { get; }

        public ICommand RegisterCommand { get; }

        private bool CanLogin()
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(Username) && HasPassword(LoginPassword);
        }

        private bool CanRegister()
        {
            return !IsBusy
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(DisplayName)
                && HasPassword(RegisterPassword)
                && HasPassword(RegisterPasswordConfirm);
        }

        private void HandleLogin()
        {
            StatusMessage = "Giris islemi hazir degil. Altyapi baglandiginda aktif olacak.";
        }

        private void HandleRegister()
        {
            if (!SecureEquals(RegisterPassword, RegisterPasswordConfirm))
            {
                StatusMessage = "Parolalar eslesmiyor.";
                return;
            }

            StatusMessage = "Kayit islemi hazir degil. Altyapi baglandiginda aktif olacak.";
        }

        private static bool HasPassword(SecureString? password)
        {
            return password != null && password.Length > 0;
        }

        private static bool SecureEquals(SecureString? left, SecureString? right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            var leftBstr = IntPtr.Zero;
            var rightBstr = IntPtr.Zero;

            try
            {
                leftBstr = Marshal.SecureStringToBSTR(left);
                rightBstr = Marshal.SecureStringToBSTR(right);

                for (var i = 0; i < left.Length; i++)
                {
                    var leftChar = (char)Marshal.ReadInt16(leftBstr, i * 2);
                    var rightChar = (char)Marshal.ReadInt16(rightBstr, i * 2);

                    if (leftChar != rightChar)
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                if (leftBstr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(leftBstr);
                }

                if (rightBstr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(rightBstr);
                }
            }
        }

        private void ClearSensitiveFields()
        {
            LoginPassword = DisposeSecureString(LoginPassword);
            RegisterPassword = DisposeSecureString(RegisterPassword);
            RegisterPasswordConfirm = DisposeSecureString(RegisterPasswordConfirm);
        }

        private void UpdateCommandStates()
        {
            if (LoginCommand is RelayCommand loginCommand)
            {
                loginCommand.RaiseCanExecuteChanged();
            }

            if (RegisterCommand is RelayCommand registerCommand)
            {
                registerCommand.RaiseCanExecuteChanged();
            }
        }

        private static SecureString? DisposeSecureString(SecureString? value)
        {
            if (value == null)
            {
                return null;
            }

            if (!value.IsReadOnly())
            {
                value.Clear();
            }

            value.Dispose();
            return null;
        }
    }
}
