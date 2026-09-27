using UnityEngine;
using UnityEngine.UI;
using TMPro;

// シーンに置くホイールUIの参照まとめ。オンライン時、プレイヤーが実行時にこれを探して使う。
public class WeaponWheelUI : MonoBehaviour
{
    public GameObject wheelPanel;
    public Image[] segments;   // WeaponWheelのSlotsと同じ順番にする
    public TMP_Text hubName;
    public TMP_Text hubIndex;
}