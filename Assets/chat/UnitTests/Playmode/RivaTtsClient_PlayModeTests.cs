// File: Assets/Tests/PlayMode/RivaTtsClient_PlayModeTests.cs
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

[TestFixture]
public class RivaTtsClient_PlayModeTests
{
    [Test]
    public IEnumerator AudioSourceStartsPlaying()
    {
        var go = new GameObject("audio-test");
        var src = go.AddComponent<AudioSource>();
        src.clip = AudioClip.Create("testClip", 44100, 1, 44100, false);
        src.Play();
        yield return null;
        Assert.IsTrue(src.isPlaying);
        Object.DestroyImmediate(go);
    }
}