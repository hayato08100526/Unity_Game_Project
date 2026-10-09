using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;

public class NetworkPlayerSpawn : NetworkBehaviour
{
    [Tooltip("ロビーのシーン名。これ以外のシーンを「試合中」とみなす")]
    [SerializeField] private string lobbySceneName = "OnlineLobbyScene";

    [Tooltip("シーンに置くスポーン地点の名前の頭。SpawnPoint_P1, SpawnPoint_P2 ... を探す")]
    [SerializeField] private string spawnPointPrefix = "SpawnPoint_P";

    [Tooltip("シーンにスポーン地点が見つからないときに使う予備の位置(Host=0番, 1人目のClient=1番...)")]
    [SerializeField]
    private Vector2[] fallbackSpawnPoints =
    {
        new Vector2(-5f, 2f),
        new Vector2(5f, 2f),
        new Vector2(-2f, 2f),
        new Vector2(2f, 2f),
    };

    private Rigidbody2D rb;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ApplyScene(SceneManager.GetActiveScene());
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        ApplyScene(newScene);
    }

    private void ApplyScene(Scene scene)
    {
        bool inGame = scene.name != lobbySceneName;

        // ロビーでは物理を止めて落下させない
        if (rb != null) rb.simulated = inGame;

        if (inGame) TeleportToSpawn();
    }

    // 自分のプレイヤーをスポーン地点へ移動(リスポーン時にも使う)
    public void TeleportToSpawn()
    {
        if (!IsOwner) return;

        Vector3 pos = GetSpawnPosition();

        if (rb != null) rb.linearVelocity = Vector2.zero;

        var netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null)
            netTransform.Teleport(pos, transform.rotation, transform.localScale);
        else
            transform.position = pos;
    }

    private Vector3 GetSpawnPosition()
    {
        // Host(OwnerClientId 0)が P1、最初の Client が P2
        int number = (int)OwnerClientId + 1;

        GameObject point = GameObject.Find(spawnPointPrefix + number);
        if (point != null) return point.transform.position;

        if (fallbackSpawnPoints.Length == 0) return Vector3.zero;
        int index = (int)(OwnerClientId % (ulong)fallbackSpawnPoints.Length);
        return fallbackSpawnPoints[index];
    }
}