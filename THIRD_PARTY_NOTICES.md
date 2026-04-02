This project includes code derived from third-party works. This document provides 
attribution and identifies which licenses apply to which parts of the codebase.

---

# License Summary

This repository contains code under multiple licenses:

- **MPL-2.0 (Mozilla Public License 2.0)**  
  Applies to:
    - PhacelleNoise method
    - ErosionFilter method

- **MIT License**  
  Applies to:
    - Code derived from Inigo Quilez, Clay John, and Fewes
    - Original code in this repository unless otherwise stated

---

# Component-Level Attribution

## 1. MPL-2.0 Licensed Components

### Advanced Terrain Erosion Filter (core algorithm)
- **Author:** Rune Skovbo Johansen
- **Source:** ShaderToy
- **URL:** https://www.shadertoy.com/view/wXcfWn
- **License:** Mozilla Public License 2.0 (MPL-2.0)
- **Copyright:** © 2025 Rune Skovbo Johansen

---

## 2. MIT Licensed Components

### Gradient noise derivative (Noised)
- **Author:** Inigo Quilez
- **Source:** ShaderToy — "Noise - Gradient - 2D - Deriv"
- **URL:** https://www.shadertoy.com/view/XdXBRH
- **License:** MIT
- **Copyright:** © 2017 Inigo Quilez

#### Applies to:
- `Noised` function and direct translations/adaptations

---

### Earlier erosion shader work (upstream inspiration)

#### Clay John
- **Source:** https://www.shadertoy.com/view/MtGcWh
- **License:** MIT
- **Copyright:** © 2020 Clay John

#### Fewes
- **Source:** https://www.shadertoy.com/view/7ljcRW
- **License:** MIT
- **Copyright:** © 2023 Fewes

#### Rune Skovbo Johansen (earlier versions)
- **Source:** https://www.shadertoy.com/view/33cXW8
- **License:** MIT
- **Copyright:** © 2025 Rune Skovbo Johansen

These works informed the technique but may not correspond to directly copied code.

---

## 3. Original Work

### AdvancedTerrainErosion (C# implementation)
- **Author:** Luke Mitchell
- **Repository:** https://github.com/lpmitchell/AdvancedTerrainErosion
- **Copyright:** © 2026 Luke Mitchell

---

# Acknowledgements

This project builds upon publicly shared shader work from the ShaderToy community.
Their contributions made this implementation possible.

If any attribution is incorrect or incomplete, please open an issue or pull request.