using UnityEngine;

// Garde la texture de police du matériau des panneaux (shader Campus/Text3D, caché par les murs) à jour :
// la police dynamique reconstruit sa texture quand de nouveaux caractères apparaissent. Placé par CampusMapBuilder.
public class SignText : MonoBehaviour
{
    public Material material;
    public Font font;

    void OnEnable()
    {
        Apply();
        Font.textureRebuilt += OnTextureRebuilt;
    }

    void OnDisable() => Font.textureRebuilt -= OnTextureRebuilt;

    void OnTextureRebuilt(Font rebuilt)
    {
        if (rebuilt == font) Apply();
    }

    void Apply()
    {
        if (material != null && font != null) material.mainTexture = font.material.mainTexture;
    }
}
