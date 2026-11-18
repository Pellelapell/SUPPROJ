using UnityEngine;

// Caméra du perso local. Deux vues, changées à la molette avec une transition :
// - vue du dessus (dézoomer) : suit le perso avec un décalage fixe ;
// - vue à la première personne (zoomer) : à hauteur des yeux, la souris tourne la vue (curseur capturé ;
//   il est libéré quand le chat, le menu ou l'écran d'un poste est ouvert).
// Focus : glisse vers une pose donnée (face à l'écran du bureau quand on est assis) puis revient à la vue choisie.
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 9f, -6.5f); // assez haut pour voir par-dessus les murs
    public float smoothing = 8f;
    [Tooltip("Vitesse du zoom vers l'écran et du retour")]
    public float focusSmoothing = 4f;
    [Tooltip("Plan proche pendant le focus : l'écran est à quelques dizaines de centimètres")]
    public float focusNearClip = 0.02f;

    [Header("Première personne")]
    public float eyeHeight = 1.6f;
    [Tooltip("Yeux un peu devant le centre du perso, pour ne pas voir l'intérieur de la tête")]
    public float eyeForward = 0.12f;
    public float lookSpeed = 3f;
    public float firstPersonNearClip = 0.05f;
    [Tooltip("Durée de la transition entre les deux vues (secondes)")]
    public float transitionTime = 0.45f;

    public static CameraFollow Instance { get; private set; }
    public bool IsFocused { get; private set; }
    public bool FirstPerson { get; private set; }
    // Avancement de la transition vers la première personne (0 : vue du dessus, 1 : à hauteur des yeux)
    public float FirstPersonBlend => blend;
    // Direction du regard à plat : les touches de déplacement s'orientent dessus en première personne
    public Quaternion Heading => Quaternion.Euler(0f, yaw, 0f);

    Camera cam;
    float nearClip;
    Vector3 focusPosition;
    Quaternion focusRotation;
    bool returning;
    Vector3 topPosition;
    float yaw, pitch, blend;
    Transform hiddenTarget;
    Renderer[] targetRenderers;
    bool bodyHidden;

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        nearClip = cam != null ? cam.nearClipPlane : 0.3f;
        topPosition = transform.position;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Focus(Vector3 position, Quaternion rotation)
    {
        focusPosition = position;
        focusRotation = rotation;
        IsFocused = true;
        returning = false;
        if (cam != null) cam.nearClipPlane = focusNearClip;
        HideBody(false);
    }

    public void Unfocus()
    {
        if (!IsFocused) return;
        IsFocused = false;
        returning = true;
    }

    public void SetFirstPerson(bool on)
    {
        if (on == FirstPerson || target == null) return;
        FirstPerson = on;
        if (on)
        {
            yaw = target.eulerAngles.y;
            pitch = 5f;
        }
    }

    // Après une téléportation (ascenseur) : la caméra saute avec le perso au lieu de glisser à travers la carte
    public void SnapTo(Quaternion heading)
    {
        if (target == null) return;
        topPosition = target.position + offset;
        yaw = heading.eulerAngles.y;
    }

    void Update()
    {
        bool blocked = IsFocused || target == null || GameChat.IsTyping || GameHud.Blocking;
        LockCursor(FirstPerson && !blocked);
        if (blocked) return;
        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SetFirstPerson(true);
        else if (scroll < 0f) SetFirstPerson(false);

        if (FirstPerson)
        {
            yaw += Input.GetAxis("Mouse X") * lookSpeed;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -70f, 70f);
        }
    }

    // En première personne le curseur est caché et bloqué au centre : la souris ne sert qu'à regarder autour
    static void LockCursor(bool on)
    {
        CursorLockMode mode = on ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        Cursor.visible = !on;
    }

    void OnDisable() => LockCursor(false);

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (IsFocused)
        {
            float k = 1f - Mathf.Exp(-focusSmoothing * dt);
            transform.position = Vector3.Lerp(transform.position, focusPosition, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, focusRotation, k);
            return;
        }
        if (target == null) return;

        // Vue du dessus : suit le perso avec un peu de retard
        Vector3 lookAt = target.position + Vector3.up * 1.2f;
        topPosition = Vector3.Lerp(topPosition, target.position + offset, smoothing * dt);
        Quaternion topRotation = Quaternion.LookRotation(lookAt - topPosition);
        // Première personne : collée aux yeux
        Vector3 eye = target.position + Vector3.up * eyeHeight + Heading * Vector3.forward * eyeForward;
        Quaternion eyeRotation = Quaternion.Euler(pitch, yaw, 0f);

        blend = Mathf.MoveTowards(blend, FirstPerson ? 1f : 0f, dt / transitionTime);
        float b = Mathf.SmoothStep(0f, 1f, blend);
        Vector3 position = Vector3.Lerp(topPosition, eye, b);
        Quaternion rotation = Quaternion.Slerp(topRotation, eyeRotation, b);
        // Son propre corps cacherait la vue (il garde son ombre)
        HideBody(blend > 0.8f);

        if (returning)
        {
            // Retour du focus : on recule en douceur, puis la vue normale reprend
            float r = 1f - Mathf.Exp(-focusSmoothing * dt);
            transform.position = Vector3.Lerp(transform.position, position, r);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, r);
            // Loin de l'écran, le plan proche d'origine ne coupe plus rien
            if (cam != null && Vector3.Distance(transform.position, focusPosition) > 1f) cam.nearClipPlane = ViewNearClip;
            if (Vector3.Distance(transform.position, position) < 0.05f) returning = false;
            return;
        }
        if (cam != null) cam.nearClipPlane = ViewNearClip;
        transform.SetPositionAndRotation(position, rotation);
    }

    float ViewNearClip => blend > 0f ? firstPersonNearClip : nearClip;

    void HideBody(bool hide)
    {
        if (target != hiddenTarget)
        {
            if (targetRenderers != null) SetShadowsOnly(false);
            hiddenTarget = target;
            targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>() : null;
            bodyHidden = false;
        }
        if (hide == bodyHidden || targetRenderers == null) return;
        bodyHidden = hide;
        SetShadowsOnly(hide);
    }

    void SetShadowsOnly(bool on)
    {
        foreach (Renderer r in targetRenderers)
            if (r != null)
                r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
