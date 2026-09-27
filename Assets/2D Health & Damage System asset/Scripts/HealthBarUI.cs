using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using ThomasDev.HealthDamageSystem;

namespace ThomasDev.HealthSystem
{
    [DisallowMultipleComponent]
    public class HealthBarUI : MonoBehaviour
    {
        [SerializeField] private Image image;

        [Tooltip("ソロ用：HPを表示する対象。オンライン時は自動で自分のプレイヤーに切り替わります")]
        [SerializeField] private GameObject gameobject;

        private Health health;

        private void Start()
        {
            // ソロプレイ時はInspectorで指定したオブジェクトを使う
            if (!IsOnline() && gameobject != null)
            {
                Bind(gameobject);
            }
        }

        private void Update()
        {
            // 既に接続済みなら何もしない
            if (health != null) return;

            // オンライン時は、自分のプレイヤーがスポーンするまで毎フレーム待つ
            if (IsOnline())
            {
                var playerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
                if (playerObject != null)
                {
                    Bind(playerObject.gameObject);
                }
            }
        }

        private static bool IsOnline()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        private void Bind(GameObject target)
        {
            health = target.GetComponentInChildren<Health>();

            if (health == null)
            {
                Debug.LogWarning($"[HealthBarUI] {target.name} にHealthコンポーネントが見つかりません。");
                enabled = false; // 警告を出し続けないよう停止
                return;
            }

            health.OnDamaged.AddListener(OnHealthChanged);
            health.OnHealed.AddListener(OnHealthChanged);

            image.fillAmount = 1f; // 開始時は満タン表示
            Debug.Log($"[HealthBarUI] {target.name} のHPに接続しました。");
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.OnDamaged.RemoveListener(OnHealthChanged);
                health.OnHealed.RemoveListener(OnHealthChanged);
            }
        }

        private void OnHealthChanged(float healthCurr, float healthMax)
        {
            image.fillAmount = healthMax > 0 ? healthCurr / healthMax : 0f;
            Debug.Log($"現在HP: {healthCurr} / 最大HP: {healthMax}（バー: {image.fillAmount}）");
        }
    }
}