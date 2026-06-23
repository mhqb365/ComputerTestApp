using System.Configuration;

namespace ComputerTestApp
{
    internal sealed class UserSettings : ApplicationSettingsBase
    {
        private static readonly UserSettings Instance =
            (UserSettings)Synchronized(new UserSettings());

        public static UserSettings Default => Instance;

        [UserScopedSetting]
        [DefaultSettingValue("vi")]
        public string Language
        {
            get => (string)this[nameof(Language)];
            set => this[nameof(Language)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("light")]
        public string Theme
        {
            get => (string)this[nameof(Theme)];
            set => this[nameof(Theme)] = value;
        }
    }
}
