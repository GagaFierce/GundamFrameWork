using WFrameWork.Core.ResLoad;
using WFrameWork.Threading;
using UnityEngine;
using UnityEngine.Audio;

namespace WFrameWork.Audio.Unity
{
    /// <summary>Compatibility wrapper; this backend consumes the shared ResourceService contract.</summary>
    public sealed class AddressablesAudioBackend : ResourceServiceAudioBackend
    {
        public AddressablesAudioBackend(ResourceService resources, Transform root, AudioMixerGroup bgmGroup = null,
            AudioMixerGroup sfxGroup = null, AudioMixerGroup uiGroup = null, IMainThreadDispatcher mainThread = null)
            : base(resources, root, bgmGroup, sfxGroup, uiGroup, mainThread) { }
    }
}
