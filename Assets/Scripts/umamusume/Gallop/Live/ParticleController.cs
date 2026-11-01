using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Component serialized on stage particle prefabs (e.g. 3d/env/live/common/confetti/
    // pfb_env_live_cmn_confetti000). Mirrors the game's Gallop.Live.ParticleController:
    //  - Initialize (0x7ffffc7fd170): caches every child ParticleSystem by name hash, then
    //    PlayEmit() and, because PauseOnStart defaults to true (ctor 0x7ffffc7fe8a0), PauseEmit().
    //  - PlayEmit (0x7ffffc7fdb00): main.simulationSpeed = _playSpeed, then Play().
    //  - StageController.UpdateParticle (0x7ffffc815730) resumes (Play(true)) or pauses
    //    (Pause(true)) the systems every frame following the live pause state.
    //  - UpdateFromTimeline (0x7ffffc7fe410): the particleList key only replaces
    //    rateOverTime.constantMax; the curve mode and constantMin authored in the prefab stay.
    //    A name equal to the controller applies to every system, otherwise to the child system
    //    with that name.
    [AddComponentMenu("")]
    public class ParticleController : MonoBehaviour
    {
        public enum Status
        {
            None = 0,
            Playing = 1,
            Pause = 2,
        }

        [SerializeField]
        [Range(0, 100)]
        private float _playSpeed = 1f;

        private ParticleSystem[] _particleSystems;
        private readonly Dictionary<string, ParticleSystem> _particleDictionary =
            new Dictionary<string, ParticleSystem>(System.StringComparer.Ordinal);
        private string _name;
        private bool _isPause;
        private bool _isInitialized;

        public bool PauseOnStart { get; set; } = true;
        public Status playStatus { get; private set; }
        public float PlaySpeed => _playSpeed;

        public void Initialize()
        {
            if (_isInitialized) return;
            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            if (_particleSystems == null || _particleSystems.Length == 0) return;

            _particleDictionary.Clear();
            foreach (var particle in _particleSystems)
            {
                if (particle == null) continue;
                string key = particle.gameObject.name;
                if (!_particleDictionary.ContainsKey(key)) _particleDictionary.Add(key, particle);
            }

            PlayEmit();
            if (PauseOnStart) PauseEmit();
            _name = gameObject.name.Replace("(Clone)", string.Empty);
            _isInitialized = true;
        }

        public void PlayEmit()
        {
            if (_particleSystems == null || playStatus != Status.None) return;
            foreach (var particle in _particleSystems)
            {
                if (particle == null || particle.isPlaying) continue;
                var main = particle.main;
                main.simulationSpeed = _playSpeed;
                particle.Play();
            }
            playStatus = Status.Playing;
        }

        public void PauseEmit()
        {
            if (_particleSystems == null || _isPause) return;
            foreach (var particle in _particleSystems)
                if (particle != null) particle.Pause(true);
            _isPause = true;
        }

        // StageController.UpdateParticle: follow the live pause state every frame.
        public void SetPaused(bool paused)
        {
            if (!_isInitialized || _particleSystems == null || playStatus == Status.None) return;
            if (paused == _isPause) return;
            foreach (var particle in _particleSystems)
            {
                if (particle == null) continue;
                if (paused) particle.Pause(true);
                else particle.Play(true);
            }
            _isPause = paused;
        }

        public void UpdateFromTimeline(string name, float emissionRate)
        {
            if (!_isInitialized || string.IsNullOrEmpty(name)) return;
            if (name == _name)
            {
                foreach (var particle in _particleSystems)
                    if (particle != null) SetEmissionRate(particle, emissionRate);
            }
            else if (_particleDictionary.TryGetValue(name, out var particle) && particle != null)
            {
                SetEmissionRate(particle, emissionRate);
            }
        }

        private static void SetEmissionRate(ParticleSystem particle, float emissionRate)
        {
            var emission = particle.emission;
            var rate = emission.rateOverTime;
            rate.constantMax = emissionRate;
            emission.rateOverTime = rate;
        }
    }
}
