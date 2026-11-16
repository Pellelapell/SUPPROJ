using Unity.Netcode;

// État (ouvert / fermé) de toutes les portes de la carte, partagé par le réseau.
// Créé par le serveur à l'arrivée dans la scène de jeu : les joueurs qui arrivent en cours de partie reçoivent
// directement l'état actuel. Seuls les admins (NetworkGameManager.IsAdmin) peuvent ouvrir ou fermer une porte.
public class DoorNetwork : NetworkBehaviour
{
    public static DoorNetwork Instance { get; private set; }

    NetworkList<bool> open;

    void Awake() => open = new NetworkList<bool>();

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer && open.Count == 0)
            for (int i = 0; i < SyncedDoor.Count; i++) open.Add(false);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen(int door) => door >= 0 && door < open.Count && open[door];

    public void RequestToggle(int door) => ToggleServerRpc(door);

    [ServerRpc(RequireOwnership = false)]
    void ToggleServerRpc(int door, ServerRpcParams rpcParams = default)
    {
        if (door < 0 || door >= open.Count) return;
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsAdmin(rpcParams.Receive.SenderClientId)) return;
        open[door] = !open[door];
    }
}
