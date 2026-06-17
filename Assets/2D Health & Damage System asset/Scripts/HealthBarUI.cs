using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using ThomasDev.HealthDamageSystem;

namespace ThomasDev.HealthSystem
{
    [DisallowMultipleComponent]
    public class HealthBarUI : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] private GameObject gameobject;

        private Health health;

        private void Awake()
        {
            gameobject.TryGetComponent<Health>(out health);
        }

        private void Start()
        {
            // イベントの登録
            health.OnDamaged.AddListener(OnHealthChanged);
            health.OnHealed.AddListener(OnHealthChanged);

            // 【追加】ゲーム開始時にも現在のHPをバーに反映させる
            // health から現在のHPと最大HPを取得して一度UIを更新しておくと親切です
            // (もしhealth側にCurrentHealthやMaxHealthというプロパティがあれば、ここで呼び出せます)
        }

        private void OnHealthChanged(float healthCurr, float healthMax)
        {
            Debug.Log($"現在HP: {healthCurr} / 最大HP: {healthMax}");

            // 割り算の分母が0になるとエラー（ゼロ除算）になるのを防ぐ安全装置
            if (healthMax > 0)
            {
                // 「現在HP / 最大HP」にすることで、0.0 〜 1.0 の正確な割合になります
                image.fillAmount = healthCurr / healthMax;
            }
            else
            {
                image.fillAmount = 0f;
            }

            Debug.Log($"バーの割合: {image.fillAmount}");
        }
    }
}