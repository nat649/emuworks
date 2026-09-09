# EmuWorks — documentation technique

Émulateur puce de la NumWorks N0110, sur Renode.

Émulateur **au niveau du microcontrôleur** : il exécute réellement le code machine
ARM Cortex-M7 du firmware NumWorks sur un STM32F730 émulé. Écran et clavier
fonctionnels. Tu peux charger le firmware que tu veux (Epsilon, Omega, Upsilon,
ton propre fork).

Le firmware **fourni ici est Omega 2.0.2**, un fork communautaire basé sur Epsilon
15.5.0 — et non Epsilon officiel, que NumWorks ne publie pas en téléchargement.
Ses drivers bas niveau sont ceux d'Epsilon 15.5.0, d'où le calage de cet émulateur ;
sa couche applicative est propre à Omega (barre « OMEGA », app RPN, apps en plus).
Pour faire tourner Epsilon officiel, compile-le avec `build-firmware-n0110.yml`.

⚠️ **Ne déplace pas ce dossier vers un chemin contenant des espaces.**
Renode 1.16 ne sait pas les lire (« Could not tokenize »). D'où `C:\EmuWorks\`.

---

## Lancer

```
C:\EmuWorks\EmuWorks.exe
```

Une seule application. Elle reunit ce qui etait eparpille dans huit fichiers
`.bat` et dans des commandes tapees a la main dans le moniteur Renode :

- **choix du firmware** dans une liste deroulante ;
- **gestion des scripts Python** : ajouter, supprimer, ouvrir dans ton editeur ;
- **l'ecran de la calculatrice**, pilote au clavier du PC ;
- **enregistrement automatique** quand tu cliques sur Arreter ou fermes la fenetre.

**Aucune fenetre Renode n'apparait.** Renode tourne avec `--disable-xwt`, pilote
par son entree standard : les touches deviennent des commandes du moniteur, et
`NumWorksDisplay` sert le framebuffer sur une socket locale (`lcd Serve 3555`) —
un octet de requete, une trame 320x240 RGB565 en reponse, plafonnee a 30 images
par seconde.

Les scripts de `rom\scripts\` sont charges dans la calculatrice au demarrage,
et ce qu'elle contient est reecrit dans ce dossier a la fermeture. Autrement dit :
**le dossier fait autorite au demarrage, la calculatrice fait autorite a la fin.**

> Renode n'expose aucun crochet « avant fermeture » a un script. D'ou un vidage
> periodique de la SRAM (`mem AutoSave`, 5 s) : la conversion en `.py` est faite
> par l'application **apres** la sortie de Renode. Perte maximale en cas de
> coupure brutale : les 5 dernieres secondes.

### Les firmwares fournis

| Nom | Contenu |
|---|---|
| `epsilon-15.5.0` | Epsilon officiel, **demarre directement sur l'accueil** |
| `epsilon-15.5.0-assistant` | idem, avec l'assistant de premiere utilisation |
| `omega-2.0.2` | fork communautaire, avec assistant |

Les deux Epsilon sont compiles par la CI depuis `numworks/epsilon` au tag 15.5.0,
sans aucune modification. La difference est une **variante de compilation**
(`make epsilon.dfu` contre `epsilon.onboarding.dfu`, cf. `apps/Makefile` lignes
17-18), pas un patch : l'emulateur repartant toujours d'un demarrage a froid,
l'assistant de langue reapparaitrait sinon a chaque lancement.

`rom\scripts\` est conserve d'un firmware a l'autre : l'adresse du stockage est
relevee a chaque demarrage, elle differe pourtant d'un firmware a l'autre.

### Ligne de commande

Les anciens scripts restent disponibles dans `ligne-de-commande\` : `EmuWorks.bat`
(meme enchainement que l'application), `firmware.bat`, `rom.bat pull|push`,
`run.bat`, `run-epsilon.bat`, `run-rom.bat`, plus `trace.bat` et `compare.bat`
qui produisent les traces de non-regression.

### Lancement manuel (avancé)


```bat
C:\EmuWorks\run.bat
```

Puis, dans le moniteur Renode : `start`

- `run.bat` restaure automatiquement `flash.bin` s'il existe
- `run.bat neuf` repart d'un firmware vierge (efface la sauvegarde)

Deux fenêtres s'ouvrent : **`lcd`** (l'écran 320×240) et **`usart3`** (console série).
Un serveur GDB écoute sur `:3333`.

## Sauvegarder / restaurer

Dans le moniteur :

```
runMacro $sauver
```

Écrit `C:/EmuWorks/flash.bin` (flash externe) **et** `sram.bin` (SRAM — donc les
scripts Python et tout le stockage utilisateur). Le prochain `run.bat` recharge les deux.


> Les snapshots Renode (`Save`/`Load`) **ne marchent pas** ici : les périphériques C#
> compilés à la volée changent d'assembly à chaque lancement, la désérialisation
> échoue avec « This snapshot is incompatible ». D'où cette sauvegarde ciblée.

## Lire et écrire la mémoire (au lieu de l'USB)

L'émulateur expose la mémoire directement en fichiers — plus simple et plus
puissant qu'émuler un contrôleur USB pour faire transiter des octets.

```
mem Save "C:/EmuWorks/zone.bin" 0x20000000 0x40000     vider une zone
mem Load "C:/EmuWorks/zone.bin" 0x20000000             la réinjecter
```

Raccourcis pour les zones du N0110 :

| Commande | Zone |
|---|---|
| `mem SaveSram` / `LoadSram` | SRAM 256 Ko @ `0x20000000` |
| `mem SaveFlash` / `LoadFlash` | flash externe QSPI 8 Mo @ `0x90000000` |
| `mem SaveInternal` / `LoadInternal` | flash interne 64 Ko @ `0x08000000` |
| `mem AutoSave "<fichier>" <secondes>` | vide la SRAM periodiquement (0 pour arreter) |
| `mem Save` / `Load` / `AutoSave` / `Shell` (chemins) | un chemin relatif est resolu depuis `EMUWORKS_BASE`, la racine du projet. **Renode ne conserve pas son repertoire de lancement** : sans cette variable un chemin relatif atterrit n'importe ou. L'application et les `.bat` la posent. |
| `mem Shell "<programme>" "<arguments>"` | lance un outil externe : le moniteur Renode n'a pas d'echappement shell, sans ca le lanceur ne pourrait pas appeler `node` |

### Scripts Python

Ils vivent dans `Ion::staticStorageArea`, en SRAM. Pour Epsilon 15.5.0 officiel :

```
mem Save "C:/EmuWorks/scripts.bin" 0x20000cf8 0x8014
mem Load "C:/EmuWorks/scripts.bin" 0x20000cf8
```

L'adresse change d'un firmware à l'autre. Pour la retrouver sur n'importe
quel build, à partir de son ELF :

```bash
node tools/find-symbol.js chemin/vers/firmware.elf staticStorageArea
```


### Gérer les scripts sans ouvrir la calculatrice

`tools/scripts.js` lit et modifie un vidage de stockage :

```bash
node tools/scripts.js list  storage.bin
node tools/scripts.js get   storage.bin squares.py mon_copie.py
node tools/scripts.js add   storage.bin mon_script.py
node tools/scripts.js del   storage.bin vieux.py
```

Cycle complet, validé : démarrer, `mem Save` le stockage, ajouter un script avec
`scripts.js add`, `mem Load` le fichier modifié — le script apparaît dans la
calculatrice, sans USB ni redémarrage. Epsilon officiel 15.5.0 arrive avec
`squares.py`, `parabola.py`, `mandelbrot.py` et `polynomial.py`.

Format Epsilon : `Magic(0xEE0BDDBA) | [taille uint16 | nom\0 | corps]... | 0 | Magic`,
le premier octet du corps étant le drapeau d’importation automatique.

## Le dossier `rom` comme calculatrice

`C:/EmuWorks/rom/` contient le firmware **et** les scripts Python en vrais
fichiers `.py`. On le manipule avec l'explorateur ou son éditeur habituel.

```bat
C:/EmuWorks/run-rom.bat        lance l'émulateur sur ce dossier
```

| Sens | Dans Renode | Dans un terminal |
|---|---|---|
| calculatrice → dossier | `runMacro $tirer` | `rom.bat pull` |
| dossier → calculatrice | `rom.bat push` | `runMacro $pousser` |

L'adresse du stockage est **retrouvée automatiquement** (recherche du magic
`0xEE0BDDBA` dans la SRAM) : elle change selon le firmware — Omega `0x20000FEC`,
Epsilon officiel `0x20000CF8`. Changer de firmware ne demande donc que de
remplacer `internal.bin` et `external.bin`.

Détails : `rom/LISEZ-MOI.md`.

> **Piège du magic de fin.** La structure `Ion::Storage` est :
> `0x0000` magic d en-tete, `0x0004` tampon de 32768 o, **`0x8004` magic de fin**,
> `0x8008` delegue, `0x800C`/`0x8010` cache de recherche. Reconstruire l image en
> effacant tout apres l en-tete detruit le magic de fin et le delegue : Epsilon
> rejette alors silencieusement le stockage et les scripts n apparaissent pas,
> alors qu un parseur maison les relit sans probleme. Il faut n effacer que le
> tampon, garder le magic de fin et le delegue, et remettre le cache a zero pour
> forcer un reparcours.

## Compiler ton propre firmware

La boucle complete, validee de bout en bout : modifier une source Epsilon, la
compiler dans le nuage, la faire tourner ici.

**1. Une branche avec les sources du tag.** Un fork GitHub n'herite pas des tags :
`checkout` avec `ref: 15.5.0` echoue dessus. Il faut donc creer une branche qui
pointe sur le commit du tag amont (les forks partagent la base d'objets, donc ca
marche sans cloner) :

```bash
SHA=$(gh api repos/numworks/epsilon/git/ref/tags/15.5.0 --jq .object.sha)
SHA=$(gh api repos/numworks/epsilon/git/tags/$SHA --jq .object.sha)   # tag annote
gh api --method POST repos/TOI/epsilon/git/refs -f ref=refs/heads/ma-branche -f sha=$SHA
```

**2. Modifier.** Sur cette branche, par l'interface web ou par l'API contenus.

**3. Compiler.**

```bash
gh workflow run "Firmware N0110 (Renode)" -R TOI/epsilon -r master -f repository=TOI/epsilon -f ref=ma-branche
```

Le champ `repository` compte autant que `ref` : sans lui le workflow compilerait
les sources amont et ignorerait la modification.

**4. Installer.** L'artifact `firmware-n0110-renode` contient quatre images :

| Fichier | Variante |
|---|---|
| `epsilon.internal.bin` / `epsilon.external.bin` | **sans** assistant de premiere utilisation |
| `epsilon.onboarding.internal.bin` / `.external.bin` | **avec** assistant |

Copier la paire choisie dans `firmwares/mon-firmware/internal.bin` et
`external.bin`, puis `firmware.bat mon-firmware`.

### Ce qui a ete verifie

La chaine a ete validee de bout en bout avec deux modifications volontairement
voyantes, sur la branche `demo-15.5.0` du fork :

| Fichier | Modification |
|---|---|
| `apps/home/controller.cpp` | fond de l'accueil : `KDColorWhite` -> `KDColor::RGB24(0xD7ECFF)` |
| `apps/calculation/base.{en,fr}.i18n` | `CalculApp` : `"Calculation"` -> `"MODIFIE"` |

Le firmware compile a partir de cette branche affichait bien un accueil bleu et
une app renommee. Il **n'est pas livre ici** : `firmwares/` ne contient que des
firmwares d'origine. La branche existe toujours sur le fork si tu veux la
reprendre.

---|---|
| `apps/home/controller.cpp` | fond de l'ecran d'accueil : `KDColorWhite` -> `KDColor::RGB24(0xD7ECFF)` |
| `apps/calculation/base.{en,fr}.i18n` | `CalculApp` : `"Calculation"` -> `"MODIFIE"` |

---

## Clavier

**Vrai clavier PC** : dans la fenêtre `lcd`, menu déroulant **Keyboard:** → `keyboard`,
puis clique dans l'écran pour lui donner le focus.

| Touche PC | NumWorks |
|---|---|
| `0`-`9` | chiffres |
| flèches | flèches |
| `Entrée` | EXE |
| `Retour arrière` | effacer |
| `Échap` | retour |
| `Début` (Home) | accueil |
| `+` `-` `*` `/` | opérateurs |
| `.` / `,` | point / virgule |
| `(` `)` | parenthèses |
| `Tab` ou `Maj` | shift |
| `Verr. Maj` | alpha |
| `x` `p` `s` `c` `t` `e` `l` `r` | x,n,t · π · sin · cos · tan · exp · ln · √ |

**Depuis le moniteur** :

```
keyboard TapKey "SEVEN"        appui bref (maintenu ~5 balayages)
keyboard PressKey "SHIFT"      appui maintenu
keyboard ReleaseKey "SHIFT"
keyboard ReleaseAll
```

Noms : `ZERO`..`NINE` (ou `"7"`), `EXE`/`ENTER`, `BACKSPACE`/`DELETE`, `SHIFT`, `ALPHA`,
`LEFT`/`UP`/`DOWN`/`RIGHT`, `OK`, `BACK`, `HOME`, `PLUS`, `MINUS`, `MULTIPLICATION`,
`DIVISION`, `DOT`, `COMMA`, `SINE`, `COSINE`, `TANGENT`, `SQRT`, `SQUARE`, `PI`, `EE`,
`ANS`, `XNT`, `VAR`, `TOOLBOX`, `LN`, `LOG`, `EXP`, `POWER`, `IMAGINARY`,
`LEFTPARENTHESIS`, `RIGHTPARENTHESIS`, `ONOFF`.

---

## Contenu du dossier

| Fichier | Rôle |
|---|---|
| `numworks_n0110.repl` | description matérielle du STM32F730 |
| `NumWorksDisplay.cs` | écran ST7789V sur bus FMC (320×240 RGB565) |
| `NumWorksKeyboard.cs` | matrice clavier 9×6 + `IKeyboard` (vrai clavier PC) |
| `NumWorksCrc.cs` | unité CRC32 du STM32 (Epsilon s'en sert réellement) |
| `NumWorksAdc.cs` | ADC1 / tension batterie (réglable) |
| `MemFile.cs` | lecture/écriture de la mémoire vers des fichiers |
| `tools/find-symbol.js` | trouve une adresse de symbole dans un ELF de firmware |
| `tools/scripts.js` | liste / extrait / ajoute / supprime des scripts Python |
| `tools/rom.js` | synchronise le dossier `rom/` avec la calculatrice |
| `tools/png.js` | convertit une capture `lcd Dump` en PNG |
| `rom.resc` | lancement depuis le dossier `rom/` |
| `numworks-commun.resc` | montage de la machine + injection des scripts (inclus par les deux suivants) |
| `numworks-embarque.resc` | ecran servi sur socket, aucune fenetre -- utilise par l'application |
| `numworks.resc` | fenetre Renode classique, pour le travail au moniteur |
| `numworks_n0110.resc` | script de lancement |
| `boottest.resc` | idem, sans fenêtre, pour la non-régression |
| `smoketest.resc` | vérifie que tout compile et charge, sans firmware |
| `epsilon.onboarding.*.bin` | firmware **Omega 2.0.2** (fork basé sur Epsilon 15.5.0) |
| `epsilon_officiel.resc` | lancement avec Epsilon 15.5.0 officiel |
| `boottest-epsilon.resc` | non-régression avec Epsilon officiel |
| `build-firmware-n0110.yml` | workflow GitHub pour compiler ton propre firmware (deux variantes) |
| `../EmuWorks.exe` | **l'application** : firmware, scripts, demarrage, enregistrement |
| `../app/` | sources C# de l'application (`dotnet publish` pour la reconstruire) |

---

## Deux firmwares fournis

| Lancement | Firmware |
|---|---|
| `run.bat` | **Omega 2.0.2** (fork basé sur Epsilon 15.5.0) |
| `run-epsilon.bat` | **Epsilon 15.5.0 officiel**, compilé depuis `numworks/epsilon` |

`compare.bat` boote les deux et écrit `trace-omega.txt` / `trace-epsilon.txt` :
c'est le meilleur test de non-régression de l'émulateur, puisque deux firmwares
indépendants doivent tourner sur le même matériel modélisé.

> **Piège du double mappage de la flash interne.** C'est la même puce vue à deux
> adresses : `0x08000000` (AXIM) et `0x00200000` (ITCM). Omega démarre par la vue
> AXIM, **Epsilon officiel par la vue ITCM** (`reset PC = 0x002001F9`). Si on les
> déclare comme deux `MappedMemory` distinctes, Epsilon officiel saute dans du vide.
> D'où le multi-enregistrement dans `numworks_n0110.repl`.

Les deux images ne sont pas structurées pareil : Omega remplace le bootloader
(image interne complète de 64 Ko, externe de 8 Mo avec en-tête `f00d c0de`), alors
que le `.dfu` officiel ne contient que 8,8 Ko d'interne — NumWorks ne reflashe pas
le bootloader, protégé en écriture.

## Changer de firmware

Remplace `epsilon.onboarding.internal.bin` (chargée à `0x08000000`) et
`epsilon.onboarding.external.bin` (chargée à `0x90000000`).

Pour compiler le tien : fork de `numworks/epsilon`, ajoute
`build-firmware-n0110.yml` dans `.github/workflows/`, onglet **Actions** →
**Firmware N0110 (Renode)** → **Run workflow** (le champ `ref` accepte un tag ou ta
branche). L'artifact contient directement les deux `.bin`.

> Vise le tag **15.5.0** : au-delà, NumWorks a retiré les drivers kernel écran/clavier
> des sources publiques, et cet émulateur est calé sur cette version.
>
> Piège : dans Epsilon 15.x le N0110 est la cible **par défaut**.
> `make epsilon.onboarding.dfu` — surtout pas `PLATFORM=device MODEL=n0110`
> (`PLATFORM=device` n'existe pas).

---

## État : ce qui est modélisé

| Élément | État |
|---|---|
| Cortex-M7, mémoires, MPU, NVIC, SysTick | ✅ |
| Écran ST7789V via FMC (2 orientations MADCTL) | ✅ |
| Matrice clavier 9×6 + clavier PC | ✅ |
| RCC / horloges, RTC, DMA1/2, timers, GPIO A–H | ✅ (modèles Renode) |
| Alimentation (ODEN→ODRDY, VOS→VOSRDY) | ✅ (`STM32_PWR`) |
| Interface flash interne | ✅ (`STM32F4_FlashController`) |
| CRC32 | ✅ (`NumWorksCrc.cs`) |
| Générateur aléatoire | ✅ (`STM32F4_RNG`) |
| ADC / tension batterie | ✅ (`NumWorksAdc.cs`, réglable) |
| UART console | ✅ |

### Batterie

Réglable depuis le moniteur, en millivolts :

```
adc SetMillivolts 3650      met la batterie en "faible"
adc Millivolts              lit la valeur courante
```

Epsilon calcule `V = 2.0 × 2.8 × DR / 4095` et classe : < 3,60 V vide, < 3,70 V
faible, < 3,80 V moyenne, sinon pleine. Défaut : 4050 mV (pleine).

> L'icône « en charge » s'affiche en permanence : Epsilon lit PE3 (bas = en charge)
> et cette entrée GPIO vaut 0 par défaut dans Renode. Purement cosmétique.

## Ce qui reste bouchonné (`Tag` = valeur constante)

Ces registres renvoient une valeur figée choisie pour débloquer le boot. Ils ne
plantent pas, mais ne réagissent pas aux écritures.

| Adresse | Nom | Valeur | Pourquoi |
|---|---|---|---|
| `0x40013820` | SYSCFG CMPCR | `0x100` | bit READY de la cellule de compensation. Le modèle `STM32_SYSCFG` de Renode **n'implémente pas** ce registre → le remplacer ferait reboucler le boot. |
| `0xA0001000` | QUADSPI | `0` | la flash externe est mappée en dur, le contrôleur n'a rien à faire |
| `0xA0000000` | FMC | `0` | contrôleur du bus écran inerte (le décodage est fait par `NumWorksDisplay`) |
| `0x50000010` | USB OTG GRSTCTL | `0x80000000` | AHBIDL forcé |
| `0x50000000+` | USB OTG (reste) | `0` | **pas d'USB** : ni DFU, ni transfert de scripts |

Conséquences restantes : pas d'USB, rétroéclairage sans effet (PWM `tim3` non
modélisé), numéro de série à zéro.

---

## Déboguer un firmware qui ne boote pas

C'est la procédure qui a permis de faire marcher celui-ci.

```bat
C:\EmuWorks\trace.bat
```

écrit `C:\EmuWorks\trace.txt`. Cherche une **adresse lue des milliers de fois avec le
même PC** : c'est une attente de bit « ready » sur un registre non modélisé.

```
[WARNING] sysbus: [cpu: 0x800A338] ReadDoubleWord from non existing peripheral at 0x40007004
```

Ajoute alors dans `numworks_n0110.repl`, section `sysbus:` / `init:` :

```
Tag <0x40007004, 0x40007007> "MON_REGISTRE" 0x00010000
```

avec le bit que le firmware attend (offset à chercher dans la datasheet STM32F7).
Relance `trace.bat` : le boot doit aller plus loin. Répète.

Signes d'un boot **réussi** dans la trace :
- aucune adresse répétée des milliers de fois avec le même PC
- PC final dans `0x9007xxxx` (boucle principale du userland)
- commandes `0x2A` / `0x2B` / `0x2C` et milliers d'écritures pixel vers `lcd`
- balayage clavier (`gpioc … InputData, returned 0x13F`)

Avec GDB :

```bash
arm-none-eabi-gdb
(gdb) target remote :3333
(gdb) monitor start
```

## Servir l'ecran a une application hote

`lcd Serve <port>` demarre un serveur TCP sur `127.0.0.1` : le client envoie un
octet, le modele repond par une trame `320*240*2` octets en RGB565, telle quelle
depuis le framebuffer. `lcd StopServing` l'arrete.

C'est ce qui permet de n'avoir qu'une seule fenetre : Renode est lance sans
interface et l'application affiche l'ecran elle-meme. Le format RGB565 est celui
de la dalle, donc `Bitmap` le prend sans conversion (`Format16bppRgb565`).

## Verifier l'apparence sans ouvrir l'application

```bat
EmuWorks.exe --apercu <trame.raw> <sortie.png> [largeur hauteur]
```

Rend le panneau ecran dans un PNG et quitte, sans ouvrir de fenetre. La trame
d'entree est un RGB888 brut de 320x240 : soit `lcd Dump`, soit ce que renvoie
le serveur d'images. C'est le seul moyen de controler l'aspect (cadre,
agrandissement, centrage) depuis un terminal.

Le dessin est dans `EcranPanel.Dessiner(Graphics, Rectangle)`, separe de
`OnPaint` exactement pour ca.

## Outils de diagnostic de l'écran

Capture d ecran (verification visuelle sans interface graphique) :

```
lcd Dump "C:/EmuWorks/ecran.raw"
```
puis, dans un terminal :
```bash
node tools/png.js C:/EmuWorks/ecran.raw ecran.png
```

Combine avec `keyboard TapKey`, cela permet de piloter la calculatrice et de
verifier ce qu elle affiche en mode console, sans ouvrir de fenetre.


Dans le moniteur (`logFile @C:/EmuWorks/x.log` d'abord pour capturer dans un fichier) :

```
lcd Stats                    compteurs globaux + commandes non gérées
lcd Rects                    les 24 derniers rectangles écrits (fenêtre, encre)
lcd Ink 31 107 79 120        compte les pixels non blancs d'une zone
```

C'est ce qui a permis de trouver le bug du miroir portrait décrit ci-dessous :
`Rects` montrait des rectangles cohérents, `Ink` prouvait que le texte était bien
écrit, et le décalage n'apparaissait qu'en comparant aux coordonnées réelles.

## Les deux orientations de l'écran (piège majeur)

Epsilon pilote la dalle dans **deux orientations**, et les axes ne veulent pas dire
la même chose dans chacune (`ion/src/device/shared/drivers/display.cpp`,
`setDrawingArea`) :

| MADCTL | CASET | RASET |
|---|---|---|
| `0xA0` paysage | `r.x()` → X écran, direct | `r.y()` → Y écran, direct |
| `0x00` portrait | `r.y()` → **Y** écran | `Width - (r.x()+w)` → **X écran MIROITÉ** |

Le portrait n'est utilisé que par `pushRectUniform` — donc les aplats : boîtes de
sélection, effacement des libellés. Oublier le miroir donne un bug déroutant : les
aplats atterrissent sur la colonne horizontalement opposée (une boîte de surbrillance
apparaît sur « Calculs » alors que « Fonctions » est sélectionné), pendant que le
texte, lui, est parfaitement placé.

Autre subtilité : Epsilon **relit l'écran** (`pullRect` → `PixelFormatSet 0x06`,
`MemoryRead 0x2E`, un mot bidon, puis 3 mots de 16 bits pour 2 pixels en RGB666).
Renvoyer `0` à ces lectures efface silencieusement ce qui est composé sur le fond.

## Modifier les périphériques C#

Le compilateur intégré de Renode 1.16 est **Mono `mcs`**, pas Roslyn, et il plante
sans message exploitable (erreur tronquée à `fichier(ligne,colonne):`). Reste sur du
C# simple : **pas** de `out var`, **pas** de gros tableaux `static readonly`, **pas**
de LINQ, **pas** d'héritage de classes Renode complexes (`BaseGPIOPort` le fait
planter).

Vérifie d'abord hors Renode avec un projet `dotnet` référençant
`C:\Program Files\Renode\bin\Infrastructure.dll` — ça attrape les vraies erreurs,
mais Roslyn est plus permissif, donc ce n'est qu'un premier filtre.

Puis, sans firmware :

```bat
cd C:\EmuWorks\renode
"C:\Program Files\Renode\bin\Renode.exe" --console --disable-xwt -e "i @smoketest.resc" -e "quit"
```

Autres pièges Renode 1.16 rencontrés : `mach create "nom"` est invalide (utiliser
`mach add` + `mach set`) ; `@ none` n'enregistre pas un périphérique (lui donner une
adresse sysbus, même bidon) ; `STM32_GPIOPort` exige une plage `<addr, +0x400>` et non
une adresse simple ; `STM32F4_RCC` exige un `rtcPeripheral` ; `$ORIGIN` n'existe pas.
L’adresse change d’un firmware à l’autre. Pour la retrouver sur n’importe quel build,
à partir de son ELF :