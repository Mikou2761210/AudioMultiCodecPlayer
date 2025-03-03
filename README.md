The following text has been translated using a translation service. I apologize for any errors.
  
  
The program structure is messy, making error identification difficult. It lacks scalability and flexibility, and as such, maintenance is challenging. Use is not recommended.  
  
  
The only class the user needs to use is basically Player.  
  
## How to Use Player  
NewPlaying(filePath, playerMode, playbackState, position = null)  
filePath: The music file to be played.  
playerMode: Specifies the type of decoder to use.  
playbackState and position: Define the state when playback starts.  
  
## Playback Control  
You can switch the playback state using Play(), Pause(), and Stop().  
  
## Retrieving Playback Information  
CurrentTime and CurrentSeconds: Get the current playback position.  
TotleTime: Get the total playback duration.  

## Audio Device Mode  
Setting AudioDeviceMode to Auto will automatically change the audio device when the PC's default audio device changes.  
Setting AudioDeviceMode to Manual will prevent automatic changes, but the functionality to retrieve and set the audio device has not yet been implemented.  


## Note

Each event of the player occurs on a separate thread, and attempting to modify the UI from these events will result in an InvalidOperationException. To update the UI safely, use Dispatcher.Invoke or Dispatcher.BeginInvoke.

Dependency:Naudio, Concentus, Concentus.Oggfile, MikouTools(https://github.com/Mikou2761210/MikouTools)
