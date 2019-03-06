// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using UnityEngine;

namespace AudioStream
{
    public class AudioStreamMinimal : AudioStreamBase
    {
        // ========================================================================================================================================
        #region Editor
        [Header("")]
        [Range(0f, 1f)]
        [Tooltip("Volume for AudioStreamMinimal has to be set independently from Unity audio")]
        public float volume = 1f;

        [Tooltip("You can specify any available audio output device present in the system.\r\nPass an interger number between 0 and 'getNumDrivers' - see demo scene's Start() and AvailableOutputs()")]
        public int outputDriverID = 0;
        #endregion

        // ========================================================================================================================================
        #region AudioStreamBase
        /// <summary>
        /// Stop playback after too many dropped frames
        /// getOpenState and update in base are still OK (connection is open) although playback is finished and starving flag is still unreliable
        /// also, allow for a bit of a grace period during which some loss is recoverable / acceptable
        /// </summary>
        int starvingFrames = 0;

        protected override void StreamStarting(int samplerate)
        {
            this.SetOutput(this.outputDriverID);

            result = system.playSound(sound, null, false, out channel);
            ERRCHECK(result, "system.playSound");

            result = channel.setVolume(this.volume);
            ERRCHECK(result, "channel.setVolume");

            this.starvingFrames = 0;
        }

        protected override bool StreamStarving()
        {
            if (channel != null)
            {
                /* Silence the stream until we have sufficient data for smooth playback. */
                result = channel.setMute(starving);
                //ERRCHECK(result, "channel.setMute", false);

                if (!starving)
                {
                    result = channel.setVolume(this.volume);
                    //ERRCHECK(result, "channel.setVolume", false);
                }
            }

            if (this.starving || result != FMOD.RESULT.OK)
            {
                if (++this.starvingFrames > 60)
                    return true;
                else
                    return false;
            }
            else
            {
                this.starvingFrames = 0;
            }

            return false;
        }

        protected override void StreamPausing(bool pause)
        {
            if (channel != null)
            {
                result = this.channel.setPaused(pause);
                ERRCHECK(result, "channel.setPaused");
            }
        }

        protected override void StreamStopping() { }

        public override void SetOutput(int _outputDriverID)
        {
            LOG(LogLevel.INFO, "Setting output to driver {0} ", _outputDriverID);

            result = system.setDriver(_outputDriverID);
            ERRCHECK(result, "system.setDriver");

            this.outputDriverID = _outputDriverID;
        }
        #endregion
    }
}