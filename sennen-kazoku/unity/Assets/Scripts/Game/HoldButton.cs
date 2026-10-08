using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SennenKazoku.Game
{
    /// <summary>누르고 있는 동안만 켜지는 버튼 (원작 R 버튼 배속).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action OnDown, OnUp;
        bool down;
        public void OnPointerDown(PointerEventData e) { down = true; if (OnDown != null) OnDown(); }
        public void OnPointerUp(PointerEventData e) { Release(); }
        public void OnPointerExit(PointerEventData e) { Release(); }
        void OnDisable() { Release(); }
        void Release() { if (!down) return; down = false; if (OnUp != null) OnUp(); }
    }
}
