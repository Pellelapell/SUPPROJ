using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Perso d'un joueur en ligne. Seul le propriétaire lit les touches ; position et animations sont synchronisées
// par ClientNetworkTransform et OwnerNetworkAnimator. F : s'asseoir / se lever (et porte pour l'admin).
public class NetworkPlayer : NetworkBehaviour
{
    public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();
    public static NetworkPlayer Local { get; private set; }

    public float walkSpeed = 1.5f;
    public float runSpeed = 5f;
    public float acceleration = 6f;
    public float turnSpeed = 540f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WaveId = Animator.StringToHash("Wave");

    public readonly NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public readonly NetworkVariable<int> Palette = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Chaise occupée (Seat.index), -1 debout
    public readonly NetworkVariable<int> SeatIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Tooltip("Distance max pour ouvrir / fermer une porte (admin)")]
    public float doorReach = 2.5f;
    [Tooltip("Distance max pour s'asseoir sur une chaise")]
    public float seatReach = 1.3f;

    Animator animator;
    OwnerNetworkAnimator networkAnimator;
    CharacterController controller;
    float currentSpeed;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponentInChildren<OwnerNetworkAnimator>();
        controller = GetComponent<CharacterController>();
        if (animator != null && animator.GetComponent<SeatedPose>() == null) animator.gameObject.AddComponent<SeatedPose>();
    }

    public Seat CurrentSeat => SeatIndex.Value >= 0 ? Seat.Get(SeatIndex.Value) : null;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        Palette.OnValueChanged += (_, index) => ApplyPalette(index);
        ApplyPalette(Palette.Value);

        // Les autres joueurs sont placés par le réseau : pas de collisions de leur côté (on se traverse entre joueurs)
        if (controller != null) controller.enabled = IsOwner;

        if (!IsOwner) return;

        Local = this;
        PlayerName.Value = new FixedString64Bytes(PlayerProfile.Name);
        Palette.Value = PlayerProfile.Palette;

        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.target = transform;
        }
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (Local == this) Local = null;
    }

    void ApplyPalette(int index)
    {
        if (NetworkGameManager.Instance != null)
            PlayerProfile.ApplyPalette(gameObject, NetworkGameManager.Instance.GetPalette(index));
    }

    // ---------- Chat ----------

    public void SendChat(string message)
    {
        if (message.Length > GameChat.MaxLength) message = message.Substring(0, GameChat.MaxLength);
        SendChatServerRpc(message);
    }

    [ServerRpc]
    void SendChatServerRpc(string message)
    {
        if (message.Length > GameChat.MaxLength) message = message.Substring(0, GameChat.MaxLength);
        ReceiveChatClientRpc(message);
    }

    [ClientRpc]
    void ReceiveChatClientRpc(string message)
    {
        if (GameChat.Instance != null)
            GameChat.Instance.AddMessage(PlayerName.Value.ToString(), message);
    }

    // ---------- Déplacement ----------

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        float dt = Time.deltaTime;
        // Pendant la saisie dans le chat (ou le menu pause), les touches ne déplacent pas le perso
        bool typing = GameChat.IsTyping || GameHud.Blocking;
        Vector3 input = typing ? Vector3.zero : ReadMoveInput();

        if (!typing && AppRole.IsAdmin && ClassStream.Instance != null)
        {
            if (Input.GetKeyDown(KeyCode.P)) ClassStream.Instance.Toggle(MediaSource.Screen);
            if (Input.GetKeyDown(KeyCode.C)) ClassStream.Instance.Toggle(MediaSource.Camera);
        }

        // Assis : on reste sur la chaise, F ou une touche de déplacement relève
        if (SeatIndex.Value >= 0)
        {
            currentSpeed = 0f;
            animator.SetFloat(SpeedId, 0f);
            if (!typing && (Input.GetKeyDown(KeyCode.F) || input.sqrMagnitude > 0.01f)) StandUp();
            return;
        }

        float maxSpeed = !typing && Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, input.magnitude * maxSpeed, acceleration * dt);

        // Première personne : Z avance dans la direction du regard, Q / D en pas de côté, le corps suit le regard
        CameraFollow view = CameraFollow.Instance;
        bool firstPerson = view != null && view.FirstPerson;
        if (firstPerson)
        {
            input = view.Heading * input;
            transform.rotation = view.Heading;
        }
        else if (input.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(input);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
        }
        if (input.sqrMagnitude > 0.01f) moveDirection = input.normalized;
        // CharacterController : bloqué par les murs, les meubles et les portes fermées
        Vector3 move = (firstPerson ? moveDirection : transform.forward) * currentSpeed;
        if (controller != null && controller.enabled)
            controller.Move((move + Vector3.down * 4f) * dt);
        else
            transform.position += move * dt;
        animator.SetFloat(SpeedId, currentSpeed);

        // Saluer seulement à l'arrêt (le salut est immobile, se déplacer l'annule)
        bool wavePressed = !typing && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space));
        if (wavePressed && currentSpeed < 0.1f) networkAnimator.SetTrigger(WaveId);

        // Devant un ascenseur : F ouvre le choix de l'étage à la souris (GameHud), un chiffre y va directement
        ElevatorStop elevator = typing ? null : NearbyElevator();
        if (elevator != null)
        {
            foreach (ElevatorStop stop in ElevatorStop.All)
                if (stop != elevator && (Input.GetKeyDown(KeyCode.Alpha0 + stop.number) || Input.GetKeyDown(KeyCode.Keypad0 + stop.number)))
                {
                    TakeElevator(stop);
                    return;
                }
            if (Input.GetKeyDown(KeyCode.F)) return; // F est pour l'ascenseur, pas pour une porte ou une chaise
        }

        // F : la porte la plus proche (admin), sinon la chaise libre la plus proche
        if (!typing && Input.GetKeyDown(KeyCode.F))
        {
            SyncedDoor door = NearbyDoor();
            Seat seat = NearbySeat();
            if (door != null) DoorNetwork.Instance.RequestToggle(door.index);
            else if (seat != null) SitDown(seat);
        }
    }

    // Ce que F ferait ici (aussi utilisé par l'HUD pour afficher l'action)
    public SyncedDoor NearbyDoor() =>
        AppRole.IsAdmin && DoorNetwork.Instance != null ? SyncedDoor.Nearest(transform.position, doorReach) : null;

    public Seat NearbySeat() => Seat.NearestFree(transform.position, seatReach, this);

    public ElevatorStop NearbyElevator() => ElevatorStop.Nearest(transform.position);

    // ---------- Ascenseur ----------

    // On réapparaît devant la porte de l'ascenseur de l'étage choisi (téléportation : pas d'interpolation chez les autres)
    public void TakeElevator(ElevatorStop destination)
    {
        currentSpeed = 0f;
        if (controller != null) controller.enabled = false;
        Vector3 position = destination.ArrivalPoint;
        Quaternion rotation = destination.ArrivalRotation;
        var networkTransform = GetComponent<NetworkTransform>();
        if (networkTransform != null && networkTransform.CanCommitToTransform)
            networkTransform.Teleport(position, rotation, transform.localScale);
        else
            transform.SetPositionAndRotation(position, rotation);
        if (CameraFollow.Instance != null) CameraFollow.Instance.SnapTo(rotation);
        if (controller != null) controller.enabled = true;
    }

    // ---------- Chaises ----------

    void SitDown(Seat seat)
    {
        currentSpeed = 0f;
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(seat.point, seat.Rotation);
        SeatIndex.Value = seat.index;
        if (ClassStream.Instance != null) ClassStream.Instance.SetWatching(true);
    }

    void StandUp()
    {
        Seat seat = CurrentSeat;
        SeatIndex.Value = -1;
        if (seat != null) transform.position = seat.StandPoint;
        if (controller != null) controller.enabled = true;
        if (ClassStream.Instance != null) ClassStream.Instance.SetWatching(false);
    }

    Vector3 moveDirection = Vector3.forward;

    // ZQSD (AZERTY), WASD (QWERTY) et flèches
    static Vector3 ReadMoveInput()
    {
        float x = 0f, z = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
        if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) z += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) z -= 1f;
        return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
    }
}
