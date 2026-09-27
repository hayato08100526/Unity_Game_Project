using System.Collections;
using UnityEngine;
using Unity.Netcode;

// ソロプレイ専用のオブジェクトに付ける。オンライン時は自動で消える。
public class SoloOnlyObject : MonoBehaviour
{
    private void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return; // ソロ時は何もしない

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null)
        {
            // ネットワークオブジェクトの場合はHostが全員の画面から消す
            if (nm.IsServer) StartCoroutine(DespawnWhenReady(netObj));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator DespawnWhenReady(NetworkObject netObj)
    {
        while (!netObj.IsSpawned) yield return null;
        netObj.Despawn(true);
    }
}