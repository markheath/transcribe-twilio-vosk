# Twilio Voice Transcriber

To run this, first you will need to purchase a Twilio phone number (can do this with trial credit if you want).

Run the server locally with `dotnet run`

Create a proxy with `ngrok http 5181`.

Then set up the Twilio webhook for that number to points at the `/voice` endpoint for the ngrok proxy. 

Then test by phoning the number and speaking (in trial mode you'll need to listen to a message and press a key first). Your words will be passed to the Vosk open source speech transcription engine and recognized phrases are written to the console.

For further details, check the Twilio developer blog for a (soon to be published) article describing the code in detail.