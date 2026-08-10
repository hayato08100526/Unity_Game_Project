using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WeaponWheel : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public string name;
        [Range(0f, 360f)] public float angle;
        public Color color = Color.white;
        public Image segment;
        public GameObject weapon;
    }

    [SerializeField] GameObject wheelPanel;
    [SerializeField] Slot[] slots;

    [SerializeField] TMP_Text hubName;
    [SerializeField] TMP_Text hubIndex;

    [SerializeField] Color dimColor = new Color(0.078f, 0.086f, 0.11f, 0.78f);
    [SerializeField] float sliceGap = 0.02f;

    [SerializeField] float sensitivity = 0.011f;
    [SerializeField] float deadZone = 0.42f;

    [SerializeField] bool slowMotion = true;
    [SerializeField] float slowScale = 0.2f;
    [SerializeField] float peekTime = 1.2f;

    Vector2 dir;
    int current = 0;
    int selected = -1;
    float peekUntil = 0f;

    public int CurrentIndex { get { return current; } }

    public bool IsOpen { get { return wheelPanel != null && wheelPanel.activeSelf; } }

    public string CurrentWeaponName
    {
        get
        {
            if (slots == null || current >= slots.Length) return "";
            return slots[current].name;
        }
    }

    void Start()
    {
        Arrange();
        if (wheelPanel != null) wheelPanel.SetActive(false);
        Equip(0);
    }

    void Arrange()
    {
        if (slots.Length == 0) return;

        float fill = (1f / slots.Length) - sliceGap;
        if (fill < 0.02f) fill = 0.02f;
        float halfDeg = fill * 360f * 0.5f;

        for (int i = 0; i < slots.Length; i++)
        {
            Image seg = slots[i].segment;
            if (seg == null) continue;

            seg.type = Image.Type.Filled;
            seg.fillMethod = Image.FillMethod.Radial360;
            seg.fillOrigin = (int)Image.Origin360.Top;
            seg.fillClockwise = true;
            seg.fillAmount = fill;


            RectTransform rt = seg.rectTransform;
            float z = halfDeg - slots[i].angle;
            while (z <= -180f) z += 360f;
            while (z > 180f) z -= 360f;
            rt.localRotation = Quaternion.Euler(0f, 0f, z);
        }
    }
        void Update()
    {
        if (Input.GetMouseButtonDown(2))
        {
            Open();
        }
        else if (Input.GetMouseButton(2))
        {
            Aim();
        }
        else if (Input.GetMouseButtonUp(2))
        {
            Close();
        }
        else
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0f) Cycle(-1);
            else if (scroll < 0f) Cycle(1);

            if (peekUntil > 0f && Time.unscaledTime > peekUntil)
            {
                peekUntil = 0f;
                if (wheelPanel != null) wheelPanel.SetActive(false);
            }
        }
    }

    void Cycle(int step)
    {
        if (slots.Length == 0) return;

        int next = current + step;
        if (next >= slots.Length) next = 0;
        else if (next < 0) next = slots.Length - 1;

        Equip(next);

        if (wheelPanel != null) wheelPanel.SetActive(true);
        peekUntil = Time.unscaledTime + peekTime;
        Refresh();
    }

    void Open()
    {
        dir = Vector2.zero;
        selected = -1;
        peekUntil = 0f;
        if (wheelPanel != null) wheelPanel.SetActive(true);
        if (slowMotion) Time.timeScale = slowScale;
        Refresh();
    }

    void Aim()
    {
        dir += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sensitivity;
        dir = Vector2.ClampMagnitude(dir, 1f);

        int picked = Pick();
        if (picked != selected)
        {
            selected = picked;
            Refresh();
        }
    }

    int Pick()
    {
        if (dir.magnitude < deadZone) return -1;

        float a = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        if (a < 0f) a += 360f;

        int best = -1;
        float bestDiff = 360f;
        for (int i = 0; i < slots.Length; i++)
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(a, slots[i].angle));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    void Refresh()
    {
        int show = selected >= 0 ? selected : current;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].segment != null)
            {
                slots[i].segment.color = (i == show) ? slots[i].color : dimColor;
            }
        }

        if (show >= 0 && show < slots.Length)
        {
            if (hubName != null)
            {
                hubName.text = slots[show].name;
                hubName.color = slots[show].color;
            }
            if (hubIndex != null)
            {
                hubIndex.text = (show + 1) + " / " + slots.Length;
            }
        }
    }

    void Close()
    {
        peekUntil = 0f;
        if (slowMotion) Time.timeScale = 1f;
        if (wheelPanel != null) wheelPanel.SetActive(false);
        if (selected >= 0) Equip(selected);
        selected = -1;
        Refresh();
    }

    void Equip(int index)
    {
        current = index;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].weapon != null) slots[i].weapon.SetActive(i == index);
        }
    }

    void OnDisable()
    {
        if (slowMotion) Time.timeScale = 1f;
    }
}



