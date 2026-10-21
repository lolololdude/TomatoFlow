namespace TomatoFlow.Models;

public enum BuildEdition
{
    Consumer,
    Normal,
    Developer
}

public static class BuildInfo
{
#if TOMATO_DEVELOPER
    public const BuildEdition Edition = BuildEdition.Developer;
#elif TOMATO_NORMAL
    public const BuildEdition Edition = BuildEdition.Normal;
#else
    public const BuildEdition Edition = BuildEdition.Consumer;
#endif

    public static bool HasAdsTab { get; } =
#if TOMATO_ADS_TAB
        true;
#else
        false;
#endif

    public static bool HasAdsSettings { get; } =
#if TOMATO_ADS_SETTINGS
        true;
#else
        false;
#endif

    public static bool HasAdvanced { get; } =
#if TOMATO_ADVANCED
        true;
#else
        false;
#endif

    public static bool HasAllAdFolders { get; } =
#if TOMATO_ALL_AD_FOLDERS
        true;
#else
        false;
#endif

    public static bool IsDeveloper => Edition == BuildEdition.Developer;

    public static string EditionName => Edition.ToString();
}
