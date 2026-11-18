# Your first test on Windows: 10 steps

For someone testing Critical Mass on a Windows PC for the first time. It takes about 30 minutes. You need the zip
from the team, a mouse and keyboard, and (if you have one) a controller.

Your answers and files stay on your computer until you send them. The game never uploads anything by itself.

## The 10 steps

1. **Download the zip** from the link the team sent you. It is called something like
   `CriticalMass-1.0.xxx-windows.zip`. Write down the number in the name: it is the build you are testing.

2. **Unzip the whole folder.** Right-click the zip, choose **Extract All**, and pick somewhere easy to find, such as
   your Desktop. Do not run the game from inside the zip: it will fail to find its files.

3. **Start the game.** Open the extracted folder and double-click `CriticalMass.exe`.

4. **If Windows shows "Windows protected your PC":** click **More info**, then **Run anyway**. Windows shows this because
   this test build is not signed yet, so Windows cannot tell you who made it. That is the honest reason: there is
   nothing hidden in it, but you are trusting the team that sent you the link. If you would rather not, stop here and
   tell the team; that is useful to know too.

5. **Read the first screens.** The first launch shows a short photosensitivity warning (press any key after a second),
   then a 47-second story film. Any key skips the film after one second. Neither shows again; WATCH INTRO on the home
   screen replays the film.

6. **Check the graphics setting.** Press Esc, open **Settings**, then **Video**. Note what **QUALITY** says (the game
   picks it for your PC). If the game feels slow later, come back here, lower it one step, and say so in your notes.

7. **Check your controller** (skip this if you have none). Plug it in by USB, or pair it by Bluetooth first. On the home
   screen, move with the stick and press the bottom face button (A, or cross). Open **Settings**, **Controls** to see
   the buttons. If nothing happens, write down the controller's name; the keyboard still works.

8. **Play one match against bots.** On the home screen choose **PLAY NOW**. Play until the results screen. After it, a
   small card asks how fun it was and what felt bad. Answering takes three presses; **SKIP** (or Esc) is fine too.

9. **Play one match online with the host.** The team will tell you when the host is ready and give you a 6-character
   room code. Choose **PLAY ONLINE**, then **JOIN ROOM**, type the code and press Enter. Join within about 30 seconds of
   getting the code; if the game says it cannot find the room, ask the host for a new code. Play until the results.

10. **Send back what you found.** Close the game, then send the team:
    - the build number from step 1;
    - your feedback file: `%USERPROFILE%\AppData\LocalLow\Drivia\Critical Mass\Replays\feedback.jsonl`
      (paste that path into the File Explorer address bar; the card's **OPEN FOLDER** button opens the same folder);
    - if anything went wrong: in the game open **Settings**, **Game**, **SAVE A PROBLEM REPORT**. It saves one zip to
      your Desktop with the game's logs. The logs alone are in `%USERPROFILE%\AppData\LocalLow\Drivia\Critical Mass\`
      (`Player.log` and the `logs` folder);
    - three sentences in your own words: what was fun, what was confusing, and anything odd.

## What to send back, in short

| What | Where |
| --- | --- |
| Build number | the zip's name |
| Feedback file | `%USERPROFILE%\AppData\LocalLow\Drivia\Critical Mass\Replays\feedback.jsonl` |
| Replays of your matches (optional, can be large) | the same `Replays` folder, `*.cmreplay` |
| Problem report (only if something broke) | Desktop, made by Settings, Game, SAVE A PROBLEM REPORT |
| Your notes | a message to the team |

The feedback file is plain text: one line per match, holding the build, your class and team, the result, the match
length, how smooth the game ran (frame times) and your answers. Open it in Notepad to see exactly what you are sending.
