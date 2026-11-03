# Starframe / 星构

A Unity project for building modular spacecraft, testing them in combat practice and revising each design. The project uses Unity 6000.3.25f1; open this folder in Unity Hub after cloning.

## Get the project

```powershell
git clone https://github.com/Sidonia704/CGDD2.git
```

Open the cloned `CGDD2` folder with Unity Hub. Unity will restore packages listed in `Packages/manifest.json` and regenerate its local `Library` cache.

## Website

The live course evidence site is published at https://sidonia704.github.io/CGDD2/. Source files are in `site/`; GitHub Pages currently serves the mirrored publication files in `docs/`. After exporting in Unity, copy the website files into `docs/` before committing and pushing so the public site updates.

## Included files

The repository includes Unity assets and metadata, scenes, project settings, package manifests, course documentation, and the website. Unity-generated caches, logs, local editor preferences, and build outputs are excluded.

## New pilot guide

One-page controls and first-sortie guides: [English](output/pdf/Starframe_Quick_Start_EN.pdf) / [中文](output/pdf/Starframe_Quick_Start_ZH.pdf).

Large reference models, HDR images and generated meshes use Git LFS. Install Git LFS before cloning, then run `git lfs pull` if large assets were not downloaded automatically. Unity regenerates Library locally; it is not versioned.
