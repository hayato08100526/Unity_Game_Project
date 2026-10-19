using Unity.Netcode;
using UnityEngine;

// オンライン対戦の「試合の状態」を2人の画面でそろえる係
// MatchManager と同じオブジェクトに、Network Object と一緒に付ける
// 決めるのはホスト(Host)だけ。クライアントは、ここに入っている値を見て画面を出す
public class OnlineMatch : NetworkBehaviour
{
    // 0 = ステージ選び / 1 = カウントダウン / 2 = 試合中 / 3 = 結果
    public const int SelectStage = 0, Countdown = 1, Playing = 2, Result = 3;

    private readonly NetworkVariable<int> state = new NetworkVariable<int>(SelectStage);
    private readonly NetworkVariable<int> stage = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<double> phaseEnd = new NetworkVariable<double>(0);   // カウントダウン / 試合が終わる時刻
    private readonly NetworkVariable<int> hostStocks = new NetworkVariable<int>(3);
    private readonly NetworkVariable<int> clientStocks = new NetworkVariable<int>(3);
    // 勝った人の番号(0 = ホスト / 1 = クライアント)、-1 = まだ、-2 = 引き分け
    private readonly NetworkVariable<int> winner = new NetworkVariable<int>(-1);

    public int State => state.Value;
    public int Stage => stage.Value;
    public int Winner => winner.Value;

    public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
    public float TimeToPhaseEnd => Mathf.Max(0f, (float)(phaseEnd.Value - Now));

    public int StocksOf(ulong clientId) => clientId == NetworkManager.ServerClientId ? hostStocks.Value : clientStocks.Value;

    // ===== ホストだけが呼ぶ =====
    public void ServerStartCountdown(int stageIndex, int stocks, float countdownSeconds)
    {
        if (!IsServer) return;
        stage.Value = stageIndex;
        hostStocks.Value = stocks;
        clientStocks.Value = stocks;
        winner.Value = -1;
        phaseEnd.Value = Now + countdownSeconds;
        state.Value = Countdown;
    }

    public void ServerStartFight(float matchSeconds)
    {
        if (!IsServer) return;
        phaseEnd.Value = Now + matchSeconds;
        state.Value = Playing;
    }

    public int ServerLoseStock(ulong clientId)
    {
        if (!IsServer) return 0;
        if (clientId == NetworkManager.ServerClientId) return --hostStocks.Value;
        return --clientStocks.Value;
    }

    public void ServerEnd(int winnerId)
    {
        if (!IsServer) return;
        winner.Value = winnerId;
        state.Value = Result;
    }

    // ===== アイテム =====
    public System.Action<int, int, Vector2> OnItemSpawned;   // 番号, 種類, 位置
    public System.Action<int, ulong> OnItemTaken;            // 番号, 取った人

    [Rpc(SendTo.Everyone)]
    public void SpawnItemRpc(int id, int kind, Vector2 pos)
    {
        OnItemSpawned?.Invoke(id, kind, pos);
    }

    [Rpc(SendTo.Everyone)]
    public void ItemTakenRpc(int id, ulong clientId)
    {
        OnItemTaken?.Invoke(id, clientId);
    }

    // ===== 全員の画面で出す演出 =====
    public System.Action<ulong, Vector2> OnKo;

    [Rpc(SendTo.Everyone)]
    public void KoRpc(ulong clientId, Vector2 pos)
    {
        OnKo?.Invoke(clientId, pos);
    }
}