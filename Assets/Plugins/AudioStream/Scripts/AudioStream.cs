// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AudioStream
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioStream : AudioStreamBase
    {
        /// <summary>
        /// autoresolved reference for automatic playback redirection
        /// </summary>
        AudioSourceOutputDevice audioSourceOutputDevice = null;

        // ========================================================================================================================================
        #region Unity lifecycle
        protected override IEnumerator Start()
        {
            yield return StartCoroutine(base.Start());

            // setup the AudioSource
            var audiosrc = this.GetComponent<AudioSource>();
            audiosrc.playOnAwake = false;
            audiosrc.Stop();
            audiosrc.clip = null;

            // and check if AudioSourceOutputDevice is present
            this.audioSourceOutputDevice = this.GetComponent<AudioSourceOutputDevice>();
        }
        

        byte[] streamDataBytes = null;
        GCHandle streamDataBytesPinned;
        System.IntPtr streamDataBytesPtr = System.IntPtr.Zero;
        float[] oafrDataArr = null; // instance buffer
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (result == FMOD.RESULT.OK && openstate == FMOD.OPENSTATE.READY && this.isPlaying && !this.isPaused && !this.finished)
            {
                if (this.streamDataBytes == null || this.streamDataBytes.Length != (data.Length * 2))
                {
                    LOG(LogLevel.DEBUG, "Alocating new stream buffer of size {0}", data.Length * 2);

                    this.streamDataBytes = new byte[data.Length * 2];

                    this.streamDataBytesPinned.Free();
                    this.streamDataBytesPinned = GCHandle.Alloc(this.streamDataBytes, GCHandleType.Pinned);

                    this.streamDataBytesPtr = this.streamDataBytesPinned.AddrOfPinnedObject();
                }

                uint read = 2;
                result = this.sound.readData(this.streamDataBytesPtr, (uint)this.streamDataBytes.Length, out read);

                // ERRCHECK(result, "OAFR sound.readData", false);

                if (result == FMOD.RESULT.OK)
                {
                    if (read > 0)
                    {
                        int length = AudioStreamSupport.ByteArrayToFloatArray(this.streamDataBytes, read, ref this.oafrDataArr);
                        for (int i = 0; i < length; ++i)
                            data[i] += this.oafrDataArr[i];
                    }
                    else
                    {
                        /*
                         * do not attempt to read from empty buffer again
                         */
                        this.finished = true;
                    }
                }
                else
                {
                    /*
                     * do not attempt to read from buffer with error again
                     */
                    this.finished = true;
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            this.streamDataBytesPinned.Free();
        }
        #endregion
        
        // ========================================================================================================================================
        #region AudioStreamBase
        protected override void StreamStarting(int samplerate)
        {
            this.GetComponent<AudioSource>().Play();

            if (this.audioSourceOutputDevice != null && this.audioSourceOutputDevice.enabled)
                this.audioSourceOutputDevice.StartRedirectWithSampleRate(samplerate);
        }

        // we are not playing the channel and retrieving decoded frames manually via readData, starving check is not used in this case
        // end of the stream is handled by readData
        protected override bool StreamStarving() { return false; }

        protected override void StreamPausing(bool pause) { }

        protected override void StreamStopping()
        {
            if (this.audioSourceOutputDevice != null && this.audioSourceOutputDevice.enabled)
                this.audioSourceOutputDevice.StopRedirect();

            this.GetComponent<AudioSource>().Stop();
        }

        public override void SetOutput(int outputDriverId)
        {
            if (this.audioSourceOutputDevice != null && this.audioSourceOutputDevice.enabled)
                this.audioSourceOutputDevice.SetOutput(outputDriverId);
        }
        #endregion
    }
}