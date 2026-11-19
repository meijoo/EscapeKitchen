**Escape House**

The game is called Escape House. It’s a VR escape room with four rooms: a bedroom, a bathroom, a living room, and a kitchen. Each room was built by a different team member in a different style. This was intentional, since we wanted to experiment with different aesthetics for our final project.

This is an early prototype of a baby escape game that we plan to develop further.

Team: Zhi, Michelle, Hyerynn, Nithya
Course: COMP 590
Engine: Unity 6 (URP)
Platform: Meta Quest 3

**Video:** https://youtu.be/dy_pN0ZRoec

**Rooms and Missions**

Each room has a mission that must be completed before its door opens to the next room.

1. Bedroom: Collect all the stars by crawling and grabbing them. Once they’re all collected, the door opens.

2. Bathroom: Find the quacking duck. The duck’s sound helps you locate it. Crawl over and grab it to open the next door.

3. Living Room: Grab the key to unlock the door. The key is high up, so you need to rearrange the furniture and objects around you into a staircase-like structure. Climb it, grab the key, and put it into the door.

4. Kitchen: Find the three bottles and place each one upright on its matching color placeholder. Once all three are in place, the final door opens and you escape.

**Scoring and Timing**

The game records your total time from the start of the bedroom to the final door. The goal is the lowest time. Our team's current record is about 4 minutes.

Why this is a game: The experience has a clear goal (escape), rules (each mission must be completed in order), challenge (puzzles that require problem solving and physical effort), and a measurable outcome (escape time). These match the definitions of games discussed in class.

Future work: We’d like to add hints that point players toward each mission without giving away the solution. We didn’t have time for this in the prototype.

**Movement**
The game uses crawling. Players use their arms to pull themselves forward.

We chose this for two reasons:

1. Reduced motion sickness: Arm-based movement is controlled by the player’s body, which keeps the view more stable and reduces the mismatch between what the eyes and body feel.
2. Fit with the theme: A baby escape game naturally uses crawling, so the movement supports the story.

**Sound**

We experimented with sound as a core part of the experience.

Duck sound (bathroom): The quacking duck is the main clue for finding it. Its volume and direction help the player locate it.

Success sound: A short sound plays when the duck mission is completed, confirming progress and signaling that a door has opened.

With additional time, we would like to experiment with more auditory and other sensory cues. We hope to do this for the final project. 

**Building Process**
Brainstorming and concept: We explored escape room ideas and settled on a baby escape theme. It gave us a clear reason to use crawling and gave each room its own personality.

Layout: We planned the house as four connected rooms, each with a mission area and a locked door to the next room.

Game elements: We defined each room’s mission, the room order, and the game components(rubber duck, bottles, etc).

Building the scenes: We mainly used pre-existing assets from the Asset Store, listed below. We also built and configured some of our own objects, like the architecture of the rooms, etc. Each team member adjusted scale, lighting, and colliders in their room so objects can be grabbed and placed correctly in VR.

Scripts with generative AI. We used Claude and Codex to help draft Unity C# scripts for interactions, puzzle logic, and door transitions. We tested, edited, and debugged all generated code in Unity. The team made the design decisions and is responsible for how the final game works.

**How to Run and Test**
Steps
1. Clone the repository: git clone [repo URL]
2. Open the project in Unity Hub using the version above.
3. Open the first scene: Assets/Scenes/[Bedroom scene].
4. Confirm the four room scenes are listed in File > Build Profiles (or Build Settings) in the correct order.
5. Press Play to test in the Editor, or build to your headset.

**Known Issues/Improvements**
- Hints for the puzzles are not implemented yet.
- More auditory cues.

**Asset Libraries and Attribution**

- Bathroom Set- Interior by Geniuscrate Games: https://assetstore.unity.com/packages/3d/props/furniture/bathroom-set-interior-263462

- Apartment Kit by Brick Project Studio: https://assetstore.unity.com/packages/3d/environments/apartment-kit-124055

- Functional Washing Machine by Pixel Pulse: https://assetstore.unity.com/packages/3d/environments/apartment-kit-124055

- Free Wood Door Pack by Biostart: https://assetstore.unity.com/packages/3d/props/interior/free-wood-door-pack-280509

- Kitchen Furniture Starterpack by Mavi3D: https://assetstore.unity.com/packages/3d/props/furniture/kitchen-furniture-starterpack-209331

- Baby Bottle Free low-poly 3D model by sam-etver-3d: https://www.cgtrader.com/free-3d-models/food/miscellaneous/baby-bottle-55a51e4a-df19-412d-9696-40ba91a3fcff

Generative AI tools

- Claude (Anthropic): helped draft Unity C# scripts and debug errors.

- Codex (OpenAI): helped draft Unity C# scripts and debug errors.


Thank you for playing!
