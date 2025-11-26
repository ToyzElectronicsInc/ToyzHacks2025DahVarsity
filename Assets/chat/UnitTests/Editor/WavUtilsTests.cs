// File: Assets/Tests/Editor/WavUtilsTests.cs
using NUnit.Framework;

[TestFixture]
public class WavUtilsTests
{
    [Test]
    public void Pcm16LeToFloatArray_ConvertsKnownSamples()
    {
        byte[] pcm = { 0x00,0x00, 0xFF,0x7F };
        var floats = WavUtils.Pcm16LeToFloatArray(pcm);
        Assert.AreEqual(2, floats.Length);
    }
}