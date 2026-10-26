using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IbArtMuseum.Remake
{
    /// <summary>
    /// PART 2 지하 연구소 복도의 기괴한 벽면 안구 군집 및 원혼(잔향체) 타겟 — 카메라 플래시 촬영 시 정화됨
    /// </summary>
    public class ResonanceAnomalyTarget : MonoBehaviour
    {
        public bool isPurified = false;
        public List<Transform> eyeballObjects = new List<Transform>();
        public Transform spiritSilhouette;
        public Light anomalyGlowLight;

        private Vector3 _initialSpiritPos;

        private void Start()
        {
            if (spiritSilhouette != null)
            {
                _initialSpiritPos = spiritSilhouette.localPosition;
            }
        }

        private void Update()
        {
            if (isPurified) return;

            float pulse = 1f + Mathf.Sin(Time.time * 5.5f) * 0.12f;

            foreach (var eye in eyeballObjects)
            {
                if (eye == null) continue;
                eye.localScale = Vector3.one * pulse;
            }

            if (spiritSilhouette != null)
            {
                spiritSilhouette.localPosition = _initialSpiritPos + new Vector3(
                    Mathf.Sin(Time.time * 2.2f) * 0.18f,
                    Mathf.Cos(Time.time * 3.1f) * 0.12f,
                    0f
                );
            }
        }

        public void PurifyByCameraFlash()
        {
            if (isPurified) return;
            isPurified = true;
            StartCoroutine(CoPurifyDissolve());
            ResonanceGameManager.Instance?.OnDistortionClusterPurified();
        }

        private IEnumerator CoPurifyDissolve()
        {
            float t = 0f;
            Vector3 startScale = transform.localScale;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.5f;
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                if (anomalyGlowLight != null)
                {
                    anomalyGlowLight.intensity = Mathf.Lerp(150f, 0f, t);
                }
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
