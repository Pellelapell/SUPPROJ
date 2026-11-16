using UnityEngine;

// Objets visibles seulement en vue à la première personne (plafond et éclairage intérieur du campus) :
// en vue du dessus ils sont masqués pour voir l'intérieur du bâtiment. Placé par CampusMapBuilder.
public class FirstPersonOnly : MonoBehaviour
{
    [Tooltip("Avancement de la transition (CameraFollow.FirstPersonBlend) à partir duquel les objets s'affichent")]
    public float showAt = 0.8f;

    Renderer[] renderers;
    Light[] lights;
    bool shown = true;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        lights = GetComponentsInChildren<Light>(true);
        Show(false);
    }

    void LateUpdate()
    {
        CameraFollow cam = CameraFollow.Instance;
        Show(cam != null && cam.FirstPersonBlend >= showAt);
    }

    void Show(bool on)
    {
        if (on == shown) return;
        shown = on;
        foreach (Renderer r in renderers) r.enabled = on;
        foreach (Light l in lights) l.enabled = on;
    }
}
