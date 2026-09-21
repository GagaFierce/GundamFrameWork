using System;
using System.Collections.Generic;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    public enum ApplicationPausePolicy { Continue, PauseAll, PauseSelectedGroups }

    /// <summary>Loop identities owned or reused by one explicitly installed Unity host.</summary>
    public readonly struct UnityFrameUpdateLoops
    {
        public UpdateLoop Input { get; }
        public UpdateLoop Simulation { get; }
        public UpdateLoop Physics { get; }
        public UpdateLoop Presentation { get; }

        public UnityFrameUpdateLoops(UpdateLoop input, UpdateLoop simulation,
            UpdateLoop physics, UpdateLoop presentation)
        {
            Input = input;
            Simulation = simulation;
            Physics = physics;
            Presentation = presentation;
        }
    }

    /// <summary>Install takes a snapshot; subsequent edits do not mutate a running host.</summary>
    public sealed class UnityFrameUpdateSettings
    {
        public ApplicationPausePolicy BackgroundPolicy { get; set; } = ApplicationPausePolicy.Continue;
        public IReadOnlyList<UpdateGroup> BackgroundGroups { get; set; } = Array.Empty<UpdateGroup>();
        public UnityFrameUpdateLoops? Loops { get; set; }
        public string HostName { get; set; } = "Frame Update Host";
    }
}
