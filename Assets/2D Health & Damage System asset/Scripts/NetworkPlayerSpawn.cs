using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;

public class NetworkPlayerSpawn : NetworkBehaviour
{
    [SerializeField] private string gameSceneName = "SoloScene";

    [Tooltip("プレイヤーごとのスポーン地点(Host=0番, 1人目のClient=1番...)")]
    [SerializeField]
    private Vector2[] spawnPoints =
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
        bool inGame = scene.name == gameSceneName;

        // Lobbyでは物理を止めて落下させない
        if (rb != null) rb.simulated = inGame;

        if (inGame) TeleportToSpawn();
    }

    // 自分のプレイヤーをスポーン地点へ移動(リスポーン時にも使う)
    public void TeleportToSpawn()
    {
        if (!IsOwner || spawnPoints.Length == 0) return;

        int index = (int)(OwnerClientId % (ulong)spawnPoints.Length);
        Vector3 pos = spawnPoints[index];

        if (rb != null) rb.linearVelocity = Vector2.zero;

        var netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null)
            netTransform.Teleport(pos, transform.rotation, transform.localScale);
        else
            transform.position = pos;
    }
}