using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SennenKazoku.Game
{
    /// <summary>집 화면 드래그(가로 스크롤) 입력.</summary>
    public sealed class HouseDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<float> OnDragX;     // 화면 px 이동량
        public Action OnBegin, OnEnd;
        public void OnBeginDrag(PointerEventData e) { if (OnBegin != null) OnBegin(); }
        public void OnDrag(PointerEventData e) { if (OnDragX != null) OnDragX(e.delta.x); }
        public void OnEndDrag(PointerEventData e) { if (OnEnd != null) OnEnd(); }
    }
}
