
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

class AudioConverter
{
	private readonly BufferedWaveProvider bufferedWaveProvider;
	private readonly IWaveProvider outputProvider;
	private readonly byte[] outputBuffer;
	private readonly WaveBuffer outputWaveBuffer;
	public AudioConverter()
	{
		bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(8000, 1));
		var resampler = new WdlResamplingSampleProvider(bufferedWaveProvider.ToSampleProvider(), 16000);
		outputProvider = new WaveFloatTo16Provider(resampler.ToWaveProvider());
		outputBuffer = new byte[16000*2]; // one second of audio should be plenty
		outputWaveBuffer = new WaveBuffer(outputBuffer);
	}
	
	public (short[],int) ConvertBuffer(byte[] input)
	{
		var samples = input.Length;

        // ulaw 8000 bitrate to Linear 8kHz bitrate
        for (int i = 0; i < input.Length; i++)
        {
            outputWaveBuffer.ShortBuffer[i] = MuLawDecoder.MuLawToLinearSample(input[i]);
        }

		bufferedWaveProvider.AddSamples(outputWaveBuffer.ByteBuffer, 0, samples*2);
		var convertedBytes = samples * 4; // to PCM and to 16kHz
		var outRead = outputProvider.Read(outputBuffer, 0, convertedBytes);
		return (outputWaveBuffer.ShortBuffer, outRead / 2);
	}
}