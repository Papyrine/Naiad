public class ThemePreferenceService(IJSRuntime jsRuntime)
{
    const string themeKey = "selectedTheme";

    public async Task<ThemeType> GetSavedThemeAsync()
    {
        var value = await jsRuntime.InvokeAsync<string?>("statePreference.get", themeKey);
        if (value is not null && Enum.TryParse<ThemeType>(value, out var theme))
        {
            return theme;
        }

        return ThemeType.Light;
    }

    public async Task SaveThemeAsync(ThemeType theme) =>
        await jsRuntime.InvokeVoidAsync("statePreference.set", themeKey, theme.ToString());
}
