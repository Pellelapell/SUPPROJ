using UnityEngine;

// Profil du joueur local (pseudo + avatar), sauvegardé entre les sessions.
public static class PlayerProfile
{
    const string NameKey = "playerName";
    const string ModelKey = "avatarModel";
    const string PaletteKey = "avatarPalette";

    public static string Name
    {
        get => PlayerPrefs.GetString(NameKey, "");
        set => PlayerPrefs.SetString(NameKey, value);
    }

    // Index dans NetworkGameManager.playerPrefabs
    public static int Model
    {
        get => PlayerPrefs.GetInt(ModelKey, 0);
        set => PlayerPrefs.SetInt(ModelKey, value);
    }

    // Index dans NetworkGameManager.palettes
    public static int Palette
    {
        get => PlayerPrefs.GetInt(PaletteKey, 0);
        set => PlayerPrefs.SetInt(PaletteKey, value);
    }

    // Les matériaux City People dont le nom commence par "people_pal" sont interchangeables (même UV)
    public static void ApplyPalette(GameObject character, Material palette)
    {
        if (palette == null) return;
        foreach (var r in character.GetComponentsInChildren<Renderer>())
            if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("people_pal"))
                r.sharedMaterial = palette;
    }
}
