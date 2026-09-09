# Ce dossier EST la calculatrice

| Fichier | Rôle |
|---|---|
| `internal.bin` | flash interne → chargée à `0x08000000` |
| `external.bin` | flash externe → chargée à `0x90000000` |
| `scripts/*.py` | les scripts Python, en vrais fichiers texte |
| `sram.bin` | vidage de travail (régénéré, ne pas éditer) |
| `.storage.bin` / `.storage.json` / `load.resc` | intermédiaires générés |
| `.scripts-precedents/` | copie de `scripts/` faite avant chaque démarrage |

## Utilisation

Lance `C:\NumWorks\NumWorks.exe`. L'application :

- injecte les `.py` de `scripts\` dans la calculatrice au démarrage ;
- réécrit `scripts\` avec ce qu'elle contient quand tu fermes sa fenêtre.

Autrement dit : **le dossier fait autorité au démarrage, la calculatrice fait
autorité à la fermeture.** Tu peux éditer `scripts\*.py` avec ton éditeur
habituel, les mettre sous git, les diffuser.

## Changer de firmware

Liste déroulante « Firmware » de l'application, puis « Installer ».

Les scripts sont conservés d'un firmware à l'autre : l'adresse du stockage est
retrouvée à chaque démarrage en cherchant le magic `0xEE0BDDBA` dans la SRAM —
elle diffère d'un firmware à l'autre (Omega `0x20000FEC`, Epsilon officiel
`0x20000CF8`).

## Filets de sécurité

Si la calculatrice a planté avant d'initialiser son stockage, réécrire le dossier
avec « rien » effacerait le travail. Deux protections :

- `rom.js pull` **refuse** de vider `scripts\` quand la calculatrice ne présente
  aucun enregistrement (message explicite ; `--force` pour passer outre) ;
- `.scripts-precedents\` garde l'état d'avant le dernier démarrage.

## Commandes manuelles (avancé)

L'application enchaîne ces étapes toute seule, mais elles restent disponibles
dans `C:\NumWorks\ligne-de-commande\` :

| | |
|---|---|
| `rom.bat pull` | `sram.bin` → `scripts\*.py` |
| `rom.bat push` | `scripts\*.py` → `.storage.bin` + `load.resc` |
| `node renode/tools/rom.js ref <sram.bin> <dossier>` | rafraîchit seulement l'image de référence, sans toucher à `scripts\` |

Dans le moniteur Renode : `mem SaveSram "..."`, `mem Load "..." <adresse>`,
`mem AutoSave "..." <secondes>`.

## Pourquoi un dossier plutôt que l'USB

Les scripts vivent en SRAM. Sur la vraie calculatrice elle reste alimentée, donc
ils survivent ; l'émulateur, lui, repart toujours à froid. C'est le dossier qui
assure la continuité — et il donne au passage ce que l'USB ne donne pas : éditer
les scripts dans son propre éditeur, les versionner, les partager.
