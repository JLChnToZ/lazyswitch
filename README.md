# Lazy Switch

This is a multi-purpose switch component for use in VRChat worlds. Here is its features:

- Maximium 32 states, each state can have infinite number of components to be controlled.
- Can switch states by ordered or randomized.
- Switch chaining, you can assign a "master switch" and when interact to either one, both will be synchroized.
- Works with both local or global (synced) mode.
- States can be saved with [Player Persistence API](https://vrc-persistence-docs.netlify.app/worlds/udon/persistence/) (currently supported in beta version)
- It supports toggling these assets/components individually:
    - Game Objects
    - Udon Behaviours
    - Renderers
    - Colliders
    - Cameras
    - Rigidbodies
    - UGUI Selectable Components (Buttons, Toggles, etc.)
    - Constraints
    - VRC Pickups
    - Custom Render Textures
    - Particle Systems (by Module)
- Easy to setup interface, adding / removing components just by clicking or dragging.
- Self contained, the script file already contains everything it need to work, except VRChat SDK and Unity.

# Installation

You can install via [VCC](https://xtlcdn.github.io/vpm/) or directly put everything inside "LazySwitch" folder to your world project.

You can getting started by dragging the provided "Lazy Switch Sample" prefab to your scene.

# LICENSE

[MIT](LICENSE)
