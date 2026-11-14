namespace ArxBeta;

public partial class App
{
    private readonly HashSet<HudKind> _hiddenHuds = [];

    internal int ActiveHudPreset => _store.Profile.ActiveHudPreset == 2 ? 2 : 1;

    internal void SaveHudPreset()
    {
        if (_closing) return;
        _store.Profile.ActiveHudPreset = ActiveHudPreset;
        SaveActiveHudPreset();
    }

    internal void SwitchHudPreset(int selected)
    {
        if (_closing || selected is not (1 or 2) || selected == ActiveHudPreset) return;
        var profile = _store.Profile;
        var current = CaptureHudPreset();
        profile.HudPresets[ActiveHudPreset] = current;
        CloseHuds();
        profile.ActiveHudPreset = selected;
        RestoreHudPreset(profile.HudPresets.GetValueOrDefault(selected, current));
        SaveActiveHudPreset();
    }

    private HudPreset CaptureHudPreset()
    {
        var profile = _store.Profile;
        foreach (var hud in _huds.Values) hud.RememberLayout();
        return new HudPreset(new(profile.HudLayouts), _huds.Keys.Concat(_hiddenHuds).ToHashSet(),
            profile.CombatHudDensity, profile.CombatHudOpacity, new(profile.CombatHudHeights));
    }

    private void RestoreHudPreset(HudPreset preset)
    {
        var profile = _store.Profile;
        profile.HudLayouts = new(preset.Windows);
        profile.CombatHudDensity = preset.CombatDensity;
        profile.CombatHudOpacity = preset.CombatOpacity;
        profile.CombatHudHeights = new(preset.CombatHeights);
        _hiddenHuds.Clear();
        foreach (var kind in preset.Visible.Where(kind => Enum.IsDefined(kind))) ShowHud(kind);
    }

    private void SaveActiveHudPreset()
    {
        var profile = _store.Profile;
        if (profile.ActiveHudPreset is not (1 or 2)) return;
        profile.HudPresets[ActiveHudPreset] = CaptureHudPreset();
        _store.Save();
    }

    private void CloseHuds()
    {
        foreach (var hud in _huds.Values.ToArray()) hud.Close();
    }

    private void ToggleAllHuds()
    {
        if (_closing) return;
        if (_hiddenHuds.Count > 0)
        {
            foreach (var kind in _hiddenHuds.ToArray()) ShowHud(kind);
        }
        else if (_huds.Count > 0)
        {
            _hiddenHuds.UnionWith(_huds.Keys);
            CloseHuds();
        }
        else ShowHud(HudKind.Combat);
    }
}
