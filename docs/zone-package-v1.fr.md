# Zone package — version expérimentale 1.1.7

Build du 15 octobre 2026, Windows x64 / .NET Framework 4.8. La nouvelle build est `CodeWalker/bin/ZoneEditor-v1.1.7/CodeWalker.exe`. Conserver tout ce dossier, notamment les DLL, `Shaders`, `icons` et le script Python. La précédente build et l'installation stable du bridge ne sont pas remplacées.

## Chargement des SLOD (1.1.7)

Le package `hw1_23_build1_asset_new` avait omis `hw1_lod_slod4` et `hw1_lod_emi_6_21_slod3` : son SLOD3 ne contenait que le proxy émissif. Dans `hw1_lod.ymap`, le modèle principal nommé `hw1_lod_slod4` est déclaré **SLOD3** ; le niveau du YMAP reste la référence, pas le suffixe du fichier.

L'ancien export arrêtait sa recherche de drawable après 10 secondes et omettait la ressource. Le nouveau chargeur attend explicitement les fichiers YDD/YDR/YFT, relance les requêtes rejetées ou évincées du cache et distingue fichier absent, entrée YDD absente et chargement en cours. Au bout de 45 secondes sans chargement, l'export s'arrête avec le nom de la ressource au lieu de produire ce niveau amputé. Un LOD absent arrête aussi l'export. Les limitations existantes pour les fragments ou les modèles skinnés restent signalées. `LODS.txt` indique chaque source, son niveau YMAP et son nombre de triangles après découpe, y compris les sources sans triangle conservé.

Validation : **687 contrôles de régression C#** et rejeu des sources SLOD3 originales contre le contour et le pivot du manifeste utilisateur. Le modèle principal contribue 1 474 triangles et le proxy 44, soit **1 518 triangles, dont 1 348 non émissifs**, contre le proxy seul dans l'ancien export. La troisième source ne conserve aucun triangle après découpe. Les versions binaire et XML concordent ; l'écart maximal au contour est de 0,0000054 m, lié à la précision flottante. Les fichiers du package utilisateur n'ont pas été remplacés. Relancer CodeWalker avec cette build, puis scanner et exporter dans un nouveau dossier pour appliquer le correctif.

## Import du package dans Blender

Correctif du script Blender du 15 octobre 2026 : le chemin du dossier du package est accepté en plus du fichier `manifest.json`. Le script trouve automatiquement le manifeste dans ce dossier, au lieu de tenter de lire le dossier comme un fichier (erreur Windows `Permission denied`). Si le manifeste manque, un message indique le fichier attendu. Aucun nouvel export ni changement de l'exécutable n'est nécessaire.

L'importeur peut également récupérer les modèles HD et leurs textures via l'API utilisée par RageQuit. Laisser **Original HD XML folder** vide et **Fetch HD via RageQuit API** coché. L'adresse et les chemins de recherche sont repris des réglages RageQuit de la scène ; sans l'addon, l'adresse par défaut est `127.0.0.1:5555`. Il faut que le service API soit lancé et configuré pour le jeu. Aucun HD n'est ajouté au package CodeWalker : les fichiers uniques sont extraits dans un sous-dossier de `%TEMP%/ragequit-blender/zone-hd`, puis placés selon le manifeste. Les nouvelles textures HD importées sont empaquetées dans Blender.

Les anciens manifestes ne donnent pas l'archive de chaque modèle HD. Si plusieurs fichiers correspondent, le script reprend le premier résultat comme le bridge RageQuit existant et consigne cet arbitrage dans `rq_zone_warnings` sur les instances du package, ainsi que `sources.json` dans le cache. Une bibliothèque XML explicite reste prioritaire et permet de choisir précisément une version. Les entrées YDD qui ne sont pas trouvées comme YDR individuels nécessitent encore cette bibliothèque. Les erreurs de recherche/extraction arrêtent l'import avant création des collections.

Validation du correctif : import réel de `hw1_23_build1_asset_new` dans Blender 5.0/Sollumz via l'API locale, avec 39 modèles distincts, 79 placements HD vérifiés, trois instances et 144 textures HD empaquetées. Les deux recherches ambiguës (`prop_weeddry_nxg02b`, `prop_weeds_nxg01b`) sont signalées. Les tests couvrent aussi les modèles absents, les réponses incorrectes, l'API inaccessible, les échecs d'extraction et la conservation de la scène ; le parcours avec bibliothèque XML reste validé.

## Import en assets et affichage des collisions (script du 15 octobre 2026)

Les props suivent les mots-clés de `rage.api_entities_auto_check` (`prop_`, `_prop`, `csx_`, `cs_x_`, `v_res_`, `v_club_`, `v_corp_`, `v_serv_`, `v_ret_`, `v_ind_`, `v_ilev_`, `v_med_`). Ils passent toujours par l'import Sollumz **en asset**, comme dans RageQuit : un mesh sans hiérarchie ni objets lumière. Les métadonnées éventuellement conservées par Sollumz restent présentes. Le script lit les critères de l'opérateur enregistré quand il est disponible, sans modifier la liste de sélection API de la scène ; sinon il utilise la même liste intégrée.

Les autres HD sont importés en assets sauf si leur XML contient des bounds, des lights ou plusieurs modèles dans un même niveau de détail : leur hiérarchie est alors conservée. Plusieurs matériaux/géométries dans un seul modèle, ou un modèle par niveau de détail, ne sont pas considérés comme plusieurs modèles à conserver. Le choix est effectué avant de retirer les bounds HD non découpés de la collection base : les collisions finales restent dans la collection Collision. Les LOD/SLOD générés sont toujours importés en assets. Les propriétés `rq_zone_import_mode` et `rq_zone_import_reason` indiquent le choix sur les objets importés.

Les meshes de collision utilisent désormais l'affichage Blender normal (`TEXTURED`, sans superposition des arêtes), au lieu du wireframe forcé. Les occlusions restent en wireframe ; l'instance Collision reste masquée initialement, comme auparavant. Ces réglages sont conservés après conversion en objets éditables.

Validation sur `hw1_23_build1_asset_new` avec le convertisseur RageQuit : **78 HD simplifiés, une hiérarchie de bâtiment avec ses lumières, quatre LOD simplifiés et 998 meshes de collision en affichage normal**. Les tests vérifient aussi les critères de props, un drawable à plusieurs modèles, les positions des 79 occurrences et la conservation des transforms des meshes/lumières après déplacement, rotation et réalisation des instances. Aucune modification de CodeWalker ni nouvel export nécessaire : recharger le script mis à jour et réimporter le package.

## Positions des primitives et couches YBN (1.1.6)

La conservation de primitives provenant d'un grand BVH gardait son ancien `GeometryCenter`, même après extraction d'un petit sous-ensemble. Les sommets pouvaient alors dépasser la plage signée de 16 bits utilisée par le YBN : le binaire déplaçait ou déformait des collisions, malgré un nombre de primitives correct. Chaque groupe conservé est désormais recentré dans son propre repère, sans modifier sa position dans le package. La vérification après sérialisation compare les positions des sommets et les transformations, en plus des compteurs.

Les collisions sont séparées selon leur source : `<nom>.ybn` (standard et props), `hi@<nom>.ybn` (haute définition) et `ma@<nom>.ybn` (couche matériau). Chaque couche non vide possède son XML et partage le même pivot. Le manifeste passe en version 3 avec une liste `collisions` ; l'importeur fourni conserve la compatibilité avec les versions 1 et 2 et importe toutes les couches directement dans la même collection Collision. La propriété `rq_zone_collision_layer` identifie la couche sur chaque objet.

Validation : 680 contrôles C#, 36 imports Blender/Sollumz en binaire et XML, plus les imports d'un package à trois couches et d'un package HI seul. Un test avec des sommets éloignés de leur centre reproduit l'échec avant correction. Le contrôle renforcé détecte également le défaut dans `hw1_23_build1_assets`. La zone exacte enregistrée dans son manifeste a été rejouée contre les neuf YBN du jeu : 31 909 triangles et 779 primitives du monde, avec vérification des positions après écriture. Ce rejeu exclut les props ; leurs transformations et primitives sont couvertes par les tests dédiés. Un nouvel export complet reste nécessaire pour remplacer l'ancien package.

## Collisions des props et primitives (1.1.5)

L'export récupère maintenant les bounds des props YDR/YDD et des fragments YFT, notamment l'archetype physique intact du LOD physique 1. Les transformations des pièces utilisent leur matrice locale et la pose d'origine du squelette, puis le placement de l'entité et le recentrage du package. Les références partagées entre drawable et archetype physique sont dédoublonnées par occurrence. L'état détruit et les collisions d'animation ne sont pas ajoutés. Le flag d'entité 4, qui désactive les collisions intégrées, est respecté.

Les **boîtes, sphères, capsules, cylindres et disques** sont pris en charge, ainsi que les boîtes/sphères/capsules/cylindres stockés comme polygones dans une géométrie YBN. Une primitive dont l'inclusion entière est confirmée reste une primitive native, avec ses transformations, matériaux et flags. Le test d'inclusion respecte aussi les creux des zones concaves ; pour les courbes, il utilise une enveloppe extérieure conservatrice et peut choisir la découpe très près d'une frontière.

Une primitive qui traverse la zone est convertie en triangles puis découpée sur le contour et les limites Z. Des faces de fermeture sont ajoutées sur les frontières de coupe, sans faces artificielles sur les arêtes internes de triangulation de la zone. Les boîtes sont triangulées exactement ; les courbes sont approchées par un maillage adaptatif visant un écart de corde de 5 mm, avec 16 à 128 segments autour de l'axe (la limite peut augmenter l'erreur pour les très grandes formes). Les primitives entièrement extérieures sont écartées. Les meshes de collision déjà triangulaires gardent leur traitement de surface précédent.

Les bounds YFT n'ont pas les filtres composites d'un YBN statique : si ces filtres sont absents, l'export applique les filtres généraux de collision du monde à la copie. Les flags existants et les matériaux sont conservés. Les physiques dynamiques de destruction ne sont pas recréées dans le YBN statique.

Le rapport et la boîte de fin distinguent **triangles**, **primitives natives conservées**, **primitives découpées** et **parties de collision de props traitées**. `COLLISIONS.txt` détaille la contribution de chaque occurrence/source après découpe, y compris les collisions désactivées par le placement. Le manifeste contient également `collision_stats`. Le YBN reste fourni en **binaire RAGE + XML**, et la relecture binaire vérifie les nombres de triangles et de primitives. L'importeur Blender conserve la séparation base/collision/LOD.

Il faut **réexporter depuis cette build** : un ancien package ne contient pas les données omises nécessaires pour recréer les collisions manquantes. Les fichiers existants et les ressources du jeu ne sont pas modifiés.

## Finalisation du dossier (1.1.4)

Windows pouvait refuser le renommage final du dossier `.partial-*` alors que toutes les ressources étaient déjà écrites. Un lecteur qui ouvre le dossier sans autoriser le partage de suppression suffit à reproduire ce cas ; le rapport seul ne permet pas d'identifier le processus qui bloquait l'export réel.

La finalisation effectue maintenant jusqu'à 12 tentatives, espacées de 250 ms, sur les erreurs d'accès/partage/verrouillage. L'attente s'effectue dans le worker, affiche une progression et reste annulable. Un fichier ou dossier de destination existant n'est jamais écrasé. Les autres erreurs restent des échecs explicites.

Si le renommage reste bloqué, le package terminé est conservé à son emplacement `.partial-*`, avec un fichier **FINALIZATION.txt** et une notification qui donne le chemin utilisable. Aucun `FAILED.txt` n'est créé pour ce seul refus de renommage. Le script Blender est copié avant la finalisation, afin qu'il soit disponible dans ce cas également. Les omissions de ressources signalées dans `REPORT.txt` restent applicables ; le champ `status: incomplete` du manifeste indique ces omissions, indépendamment de la réussite de l'écriture.

Un ancien `.partial-*` accompagné de `FAILED.txt` n'est pas automatiquement valide : les ressources doivent être vérifiées avant récupération. Le package réel `hw1_23_build1.partial-3ab96b89` a passé 345 contrôles de relecture binaire/XML (géométries, indices, shaders, données DDS, entités, YTYP, collisions et occlusions). Il contenait 4 LOD, 80 références HD, 5 055 triangles de collision et 1 362 triangles d'occlusion. Une copie récupérée a été créée séparément, avec contrôle SHA-256 des 321 fichiers copiés, conservation du dossier d'origine et archivage de l'erreur dans `RECOVERED-ERROR.txt`.

## Références HD et textures de LOD (1.1.3)

Les HD et props (niveaux HD et ORPHANHD) sont désormais référencés dans le YMAP avec leur archetype d'origine. Aucun YDR HD ni texture HD n'est copié. Leurs positions deviennent `position_source - pivot`, leurs rotations et échelles sont conservées. Le scan retient les HD dont l'origine se trouve dans le prisme ; **Add viewport selection** permet d'inclure explicitement ceux dont l'origine est dehors. Ces modèles restent entiers, avec leurs matériaux, lights, archetypes temporels et extensions d'origine. Ils dépendent des ressources du jeu/de la bibliothèque source. Les MLO restent exclus.

Les LOD/SLOD, collisions et occlusions restent reconstruits et découpés. Le conflit de textures homonymes `prop_lod` est traité par renommage des variantes différentes et mise à jour des références des matériaux, uniquement dans les copies générées. Les variantes identiques sont mutualisées ; les noms générés évitent les noms et hashes déjà réservés. Des tests relisent le binaire et le XML et vérifient les données de texture attribuées à chaque shader.

Chaque ressource générée est fournie en **binaire RAGE + XML** : YMAP d'entités, YMAP d'occlusions, YTYP des LOD générés, YDR de chaque niveau produit et YBN si des collisions prises en charge subsistent. Le manifeste passe en version 2, avec une liste `references` pour les placements HD et une liste `assets` pour les fichiers générés. Le script Blender lit les versions 1 et 2.

## Correctif export YDR (1.1.2)

La découpe pouvait modifier le tableau des géométries sans remettre à jour les trois compteurs du modèle. Le calcul des tailles et des offsets binaires utilise ces compteurs avant l'écriture : une géométrie retirée provoquait une `IndexOutOfRangeException` dans `DrawableModel.BlockLength`. Une géométrie divisée en plusieurs blocs pouvait aussi produire une disposition binaire incorrecte.

La reconstruction synchronise maintenant les compteurs, la table de pointeurs, les bounds et les associations de shaders avant la sérialisation. Chaque YDR est relu et ses nombres de modèles, géométries, sommets, indices et associations de shaders sont contrôlés avant validation. Le rapport `FAILED.txt` et la boîte d'erreur indiquent également l'opération et la ressource en cours.

L'exception rapportée a été reproduite par un test avant correction. Deux régressions passent après correction : deux géométries réduites à une, et une géométrie divisée en deux buffers (60 003 indices conservés). Les avertissements concernant les ressources absentes/non prises en charge restent distincts de ce correctif. Un ancien dossier `.partial-*` reste un export incomplet ; relancer l'export avec la nouvelle build pour obtenir un package terminé.

## Correctif scan (1.1.1)

La v1.1 pouvait rester bloquée sur « World files are still loading » : le cache accepte au plus dix demandes en attente, et le scan ne redemandait pas les fichiers refusés lorsque cette file était pleine. Le chargement utilise maintenant au plus quatre demandes simultanées, rappelle les getters du cache jusqu'au chargement et récupère les nouvelles références si un fichier non chargé a été évincé. Les fichiers chargés sont conservés pendant le scan. La progression affiche les compteurs YMAP/YBN et le chemin du fichier attendu.

Le délai de 45 secondes s'applique à chaque fichier actif, pas à l'ensemble de la zone. En cas d'échec persistant, le message identifie la ressource ; le scan s'arrête sans la considérer comme exportée. La zone dessinée et la liste précédente restent disponibles, mais un nouveau scan réussi est requis avant export. Les fichiers ouverts dans le projet prennent priorité avant de demander leurs homologues du jeu. Le chargement des dictionnaires de textures à l'export utilise la même logique de relance.

La correction exige de lancer le nouvel exécutable ; elle ne modifie pas une instance déjà ouverte. Le dessin de la zone n'est pas transféré entre instances.

## Essai dans CodeWalker

1. Ouvrir la carte et attendre la fin du chargement. Ouvrir **Tools > Project window** ; le panneau **RageQuit Bridge** contient l'onglet **Zone package**.
2. Sélectionner un bâtiment dans le viewport, puis **Use selection as pivot**. Son origine devient le zéro du package, son nom est proposé dans le champ supérieur. Le recentrage est une translation : les axes du monde sont conservés.
3. Pour partir des objets sélectionnés, cliquer **Pre-draw from viewport selection**. Sinon régler **Minimum Z** et **Maximum Z**, puis tracer manuellement. Une vue plongeante facilite les déplacements XY.
4. **Draw / continue polygon**, puis clic gauche sur les surfaces pour chaque sommet. **Entrée**, clic droit ou **Finish polygon** ferme la zone et passe à l'édition des points. **Escape** quitte l'édition ; **Backspace/Ctrl+Z** annule une modification de points. Voir les commandes ci-dessous.
5. **Scan zone** collecte les candidats depuis les index spatiaux des YMAP/YBN. Les HD sont retenus selon leur origine dans le prisme. La liste des LOD est volontairement large ; leur découpe exacte intervient à l'export. Les variantes YMAP dépendent des filtres de disponibilité actuels de CodeWalker. Les fichiers du projet ouvert prennent priorité sur ceux du jeu.
6. Ajouter des occurrences sélectionnées dans le viewport avec **Add viewport selection** ; utiliser **Exclude highlighted entries** pour retirer des lignes. Les HD ajoutés restent entiers, même si leur origine est hors zone ; les ajouts LOD restent soumis à la découpe. **Clear zone** efface aussi ces corrections.
7. **Export new package…**, choisir un dossier parent. Le nom doit utiliser 1 à 48 lettres minuscules, chiffres ou `_`. Un sous-dossier portant ce nom est créé ; un dossier existant n'est jamais écrasé. Éviter d'éditer les sources pendant l'export.
8. Lire **REPORT.txt**. Les omissions et textures introuvables y figurent. En cas d'échec ou d'annulation, un dossier `.partial-*` et `FAILED.txt` conservent le diagnostic ; ce dossier n'est pas considéré comme terminé. L'exception est un refus persistant de renommage après écriture réussie : le message de succès et `FINALIZATION.txt` désignent alors le dossier utilisable malgré son suffixe `.partial-*`.

Commencer par un petit bâtiment statique et vérifier visuellement les coupes, les textures et les lights avant de constituer une bibliothèque.

## Création et édition de zone (1.1)

**Pré-dessin.** L'outil projette les triangles du meilleur niveau interne disponible des meshes statiques sélectionnés sur XY, calcule leur couverture puis suit le contour extérieur. Il conserve les creux d'un contour concave, au lieu de prendre une boîte englobante. **Contour precision (m)** règle la grille (0,10 m par défaut, minimum 0,02 m). La taille de grille est limitée à environ 2048 × 2048 : pour une très grande sélection, la précision est automatiquement adaptée et affichée dans le bilan. Le contour est simplifié, avec une tolérance initiale d'une demi-cellule, jusqu'à respecter 256 points. Ce pré-dessin est donc une approximation réglable ; contrôler les petites saillies avant découpe.

Les objets disjoints ou un contour ambigu sont entourés par une enveloppe convexe commune, avec un message explicite. Les trous intérieurs sont remplis : la zone d'export accepte un seul contour extérieur. Min/max Z sont proposés à partir des meshes, avec une marge de 5 cm. Les objets sélectionnés sont aussi ajoutés à la sélection d'export.

**Placement au clic.** Le rayon est recalculé à partir du pixel exact du clic et de la caméra courante, puis intersecté avec les triangles rigides effectivement affichés, en conservant la surface la plus proche. Les meshes masqués/proxies non affichés sont exclus. Les échelles non uniformes et transformations rigides de modèle sont prises en compte. Les déformations skinnées/cloth et les trous dus uniquement à l'alpha des textures ne sont pas évalués. Sans surface touchée, aucun point n'est ajouté. **Alt+clic** utilise explicitement le plan **Drawing plane Z** ; décocher **Place clicks on mesh surfaces** rend ce placement planaire permanent.

**Mode Zone points.** Accessible via **Edit zone points**, le menu de sélection de la barre d'outils ou la liste des modes de sélection. Les autres sélections, gizmos et raccourcis d'édition du monde sont suspendus pendant ce mode. Le contour possède des poignées bleues ombrées de taille constante à l'écran, rendues dans le buffer de dessin du viewport, sans entité ni ressource GTA supplémentaire. La poignée sélectionnée devient orange.

| Action | Commande |
| --- | --- |
| Choisir et déplacer un point | Clic gauche puis glisser la poignée ; déplacement XY à sa hauteur initiale |
| Accrocher un point à une surface | Maintenir **Shift** pendant le déplacement |
| Ajustement numérique | Champs **Point X**, **Point Y**, **Handle Z** |
| Ajouter un point sur une arête | **Ctrl+clic** près d'une arête du contour |
| Supprimer le point sélectionné | **Delete** ou bouton du panneau |
| Annuler une modification de points | **Ctrl+Z**, **Backspace**, ou **Undo zone point edit** |
| Annuler le glissement en cours | **Escape** pendant le glissement |
| Tourner la caméra en édition | Glisser avec le bouton droit ; molette pour zoomer |
| Revenir à la sélection des objets | **Escape**, **Entrée**, **Exit zone editing / select objects**, ou choisir un autre mode |

Les poignées conservent la hauteur de la surface cliquée pour rester sous le curseur. La zone exportée reste un prisme XY : **Handle Z** positionne la poignée, tandis que **Minimum Z / Maximum Z** déterminent les coupes en hauteur. Modifier **Drawing plane Z** réaligne toutes les poignées sur ce plan. Les données de dessin ne sont pas encore sauvegardées comme un projet de zone réouvrable ; le manifeste conserve la zone lors de l'export.

**Affichage.** Le volume a des faces bleues légèrement transparentes, des contours et des poignées affichées au premier plan pendant l'édition. Le remplissage est recalculé à chaque modification. Un polygone invalide (croisement, points superposés, hauteur vide) devient rouge et perd son remplissage ; l'export refuse cette zone jusqu'à correction. L'annulation de points n'annule pas les changements de pivot ou des bornes Z.

## Fichiers produits

| Fichier | Contenu |
| --- | --- |
| `<nom>.ymap` | Références HD d'origine recentrées, LOD générés et liens LOD reconstruits |
| `<nom>_occl.ymap` | Occlusions seules, découpées en triangles |
| `<nom>.ytyp` | Définitions des nouveaux drawables |
| `<nom>_lod.ydr`, `_slod1.ydr`, etc. | Regroupement des sources présentes par niveau de LOD de carte |
| `<nom>.ybn` | Collisions centralisées : triangles découpés et primitives natives conservées |
| `*.xml` et dossiers de textures DDS | Versions destinées à l'import Sollumz |
| `manifest.json` | Fichiers, rôles, pivot d'origine, zone et avertissements |
| `import_zone_package.py` | Import et conversion en objets éditables dans Blender |
| `COLLISIONS.txt` | Contribution de chaque source/placement au YBN, après découpe |
| `LODS.txt` | Sources des LOD, niveaux YMAP et triangles conservés après découpe |

Les positions sont intégrées dans les nouvelles géométries LOD ; leurs occurrences YMAP ont une transformation identité. Les références HD conservent une transformation relative au pivot. Les sources du jeu restent intactes. Les textures résolues sont intégrées dans les YDR LOD ; les variantes homonymes différentes sont renommées et leurs shaders mis à jour.

La coupe crée des sommets aux intersections et interpole les attributs, dont les UV. Pour les meshes de drawable, les meshes de collision existants et les occlusions, elle ne ferme pas les surfaces ouvertes. Les primitives de collision converties reçoivent des faces de fermeture sur les limites de la zone. Les boîtes d'occlusion deviennent des surfaces triangulées découpées.

## Import dans Blender

Cette v1 fournit un script indépendant utilisant Sollumz ; elle ne modifie pas l'addon ragequit installé.

1. Activer **Sollumz**. Dans l'espace **Scripting**, ouvrir `import_zone_package.py` et cliquer **Run Script**.
2. Choisir le `manifest.json` du package. Pour les références HD, laisser **Original HD XML folder** vide et **Fetch HD via RageQuit API** coché pour utiliser le service RageQuit, ou choisir un dossier de modèles d'origine `.ydr.xml`/`.yft.xml` et de textures DDS, exploré récursivement. Les entrées YDD doivent être disponibles comme YDR individuels dans cette bibliothèque. Les fichiers nécessaires sont vérifiés avant de modifier la scène ; les modèles absents sont signalés. Pour importer ensuite un autre package dans la même session : **F3 > Import CodeWalker Zone Package**.
3. L'import crée trois collections sources marquées comme assets : `<nom>_assets` (meshes et drawables), `<nom>_collision` et `<nom>_lod`, rangées sous `<nom>_sources` dans l'Outliner. Les meshes LOD/SLOD sont directement dans `<nom>_lod`. Collisions standard/HI/matériau et occlusions sont directement dans `<nom>_collision`, sans sous-collections. Les hiérarchies d'objets nécessaires aux bounds sont conservées. Ce sont les collections réellement référencées par les instances : modifier leurs objets met à jour toutes les instances.
4. Les trois instances sont directement à la racine **Scene Collection** dans l'Outliner, à l'origine commune, sans collection de placement ni Empty de contrôle parent. Chaque instance se déplace indépendamment ; sélectionner les trois pour transformer tout le package ensemble. Les instances collision et LOD sont masquées au départ ; les afficher via l'Outliner pour inspection ou sélection dans le viewport. Masquer les autres meshes LOD dans l'Outliner pour n'afficher qu'un niveau à la fois. Le niveau reste indiqué par le nom du mesh et sa propriété `rq_zone_lod_level`.
5. Sélectionner les instances à convertir, puis **F3 > Make Zone Package Editable**. Les objets remplacent uniquement ces instances en conservant leur placement et les hiérarchies internes nécessaires, sans ajouter d'Empty parent. Les autres instances et les collections sources restent disponibles. L'option **Independent meshes** copie aussi les données ; sinon les meshes restent partagés. Les objets techniques restent masqués après conversion. Les anciens imports avec Empty de contrôle restent pris en charge.
6. Sauvegarder le `.blend` dans un dossier déclaré comme bibliothèque d'assets Blender. Les trois collections peuvent ensuite être instanciées séparément au même pivot. Le script ne sauvegarde pas automatiquement et ne génère pas encore d'asset unique pilotant les trois collections par un seul glisser-déposer.

### Vignettes des assets

L'option **Generate asset previews**, activée par défaut dans le sélecteur d'import, crée une vignette pour chaque collection source et son instance. Blender ne sait pas générer automatiquement l'aperçu d'un objet Empty instanciant une collection ; le script rend donc ses meshes dans une scène temporaire, en vue trois quarts avec éclairage studio et fond transparent. La vignette est intégrée au datablock Blender et reste présente lorsque l'instance est ensuite marquée **Mark as Asset**. Les sources, leur visibilité et leurs transformations restent intactes. Pour la collection LOD, la vignette montre le niveau le plus détaillé présent, sans superposer les différents niveaux. Un échec de vignette est signalé sans annuler l'import.

Pour une scène déjà importée : recharger `import_zone_package.py`, exécuter le script puis annuler le sélecteur d'import. Sélectionner l'instance dans la scène et lancer **F3 > Generate Zone Asset Preview**. Plusieurs instances sélectionnées peuvent être traitées ensemble. Cette commande remplace leur vignette ainsi que celle de leur collection source éditable. Aucun nouvel export ou import n'est requis.

Pour choisir son cadrage ou utiliser les textures du viewport, Blender 5.0 dispose d'une [capture native de vignette](https://docs.blender.org/manual/en/5.0/editors/asset_browser.html#bpy-ops-asset-screenshot-preview) :

1. Ouvrir le `.blend` source de la bibliothèque et cadrer le bâtiment dans le viewport, avec le mode d'affichage souhaité.
2. Dans l'Asset Browser, choisir **Current File**, sélectionner l'asset à modifier puis ouvrir la barre latérale avec **N**.
3. Dans **Preview**, ouvrir le menu à droite de la vignette et choisir **Capture Screenshot Preview**.
4. Tracer un rectangle entièrement dans le viewport autour du bâtiment. Blender remplace la vignette de l'asset sélectionné ; la collection et l'instance restent deux assets distincts si les deux sont marquées.
5. Sauvegarder le `.blend` pour conserver la vignette dans la bibliothèque. Refaire la capture autant que nécessaire.

Validation : trois aperçus non vides sur une fixture et sur le bâtiment réel, commande de régénération, conservation des sources et de l'état de scène, puis sauvegarde/réouverture vérifiant les pixels des vignettes de collection et d'instance. La capture interactive utilise la commande native Blender ; elle n'est pas remplacée par un opérateur personnalisé.

Le dossier `<nom>_sources` est masqué avec l'œil au départ pour éviter de superposer les originaux aux instances. Pour modifier la source sans casser les instances, sélectionner une instance puis **F3 > Show Zone Package Sources** : la collection correspondante devient accessible dans l'Outliner. Modifier ses meshes/drawables à l'origine commune, puis remasquer `<nom>_sources` avec son œil. Les sources de collision et LOD ont aussi leur propre œil, masqué initialement. Seul le dossier englobant est exclu du rendu ; les collections instanciées restent rendues normalement.

La commande fonctionne également pour les anciens imports dont les sources n'étaient pas liées à la scène, et pour une instance de collection de zone déposée depuis l'Asset Browser. Elle relie les collections existantes sans dupliquer les objets ou les données. Pour l'utiliser dans une scène déjà importée : recharger le script, l'exécuter, annuler le sélecteur d'import, puis utiliser la commande F3. Aucun nouvel export ou import du package n'est nécessaire. **Make Zone Package Editable** reste disponible pour casser les instances quand c'est souhaité.

Validation : présence des sources dans l'Outliner, modification d'un sommet répercutée dans deux instances, récupération d'un ancien import, commandes répétées sans doublons, instance sans contrôle parent issue du workflow Asset Browser et nouvel import avec une collection source active mais masquée. Deux rendus Cycles comparés pixel par pixel confirment que l'ajout des sources à la scène ne double pas la géométrie et n'empêche pas le rendu des instances.

Ne pas importer une seconde fois le YMAP d'entités dans Blender : les positions sont déjà appliquées par le script (géométrie LOD recentrée et placements des références HD). Les collisions intégrées aux modèles HD importés sont retirées de la collection base pour utiliser le YBN découpé du package. Après réouverture de Blender, relancer le script pour retrouver ses commandes F3.

## Périmètre et limites

- **Implémenté :** polygone + hauteur, ajout/exclusion d'entités, références HD d'origine recentrées, découpe des LOD statiques YDR/entrées YDD, collisions de props YDR/YFT, primitives conservées ou découpées, collisions triangulaires et occlusions ; regroupement par niveau de LOD de carte ; deux YMAP séparés ; import en trois collections et réalisation des instances.
- **Lumières :** les ressources HD référencées par le YMAP conservent leurs lights d'origine sans coupe. Dans Blender, les HD hors props avec lumières gardent leur hiérarchie ; les props et LOD sont importés en meshes seuls conformément au choix décrit ci-dessus. Pour les fichiers LOD reconstruits, les lights de drawable dont la position est dans le volume sont conservées et recentrées. Leur portée lumineuse n'est pas découpée. Les plans de culling des lights nécessitent une vérification, signalée dans le rapport. Les lumières LOD/distant stockées directement dans les YMAP sont omises.
- **Limites de reconstruction :** MLO, LOD fragments YFT ou avec squelette, bounds Cloth et types non reconnus, extensions des LOD, échelles négatives des LOD. Les time archetypes des LOD générés sont convertis en variantes statiques avec avertissement. Les collisions de fragments correspondent à l'état intact et à la pose d'origine. Le rapport ne garantit donc pas une copie exhaustive de toute la hiérarchie GTA.
- **Collisions HD d'origine :** dans un chargement RAGE du YMAP, une référence HD conserve aussi les collisions intégrées à sa ressource d'origine ; elles peuvent coexister avec le YBN généré. Dans Blender, le script retire ces bounds d'origine et utilise le YBN découpé. Les limites d'extraction du YBN restent indiquées dans le rapport.
- La recherche dépend des index spatiaux et des ressources disponibles dans le cache. Elle ne suit pas encore récursivement tous les parents LOD d'une entité ajoutée manuellement ; vérifier la liste des candidats et les niveaux produits.
- Les LOD sont extraits des sources trouvées, pas générés par décimation. Les distances de transition sont des valeurs initiales à ajuster. Aucune validation en jeu/FiveM n'a encore été effectuée.
- Les collisions reconstruites conservent les matériaux et principaux flags de bounds ; la topologie, les voisinages et les BVH sont reconstruits. Les données fines de collision et le comportement physique doivent être contrôlés sur des assets réels.
- La conversion Blender ne gère pas les instances imbriquées ajoutées manuellement après import. Elle les refuse sans supprimer les instances d'origine.

## Validation et rebuild

Les tests C# vérifient les coupes convexes/concaves, l'interpolation, les limites Z, l'absence de doublons sur les diagonales internes, la préservation des sources et la relecture des fichiers natifs YDR/YBN/YMAP/YTYP. Un test d'intégration Blender importe un package de contrôle, vérifie les trois collections et réalise les instances après déplacement/rotation.

Résultat v1.1 : **371 vérifications C# géométrie/export** et **11 vérifications du panneau d'édition**. Les tests supplémentaires couvrent le contour concave, plusieurs objets disjoints, l'annulation, les rayons perspective/orthographique, les meshes tournés avec échelle non uniforme, l'insertion/suppression de points, les instantanés de rendu et la conservation du Z cliqué. Le test du panneau instancie les contrôles sans ouvrir la carte ; il ne valide pas visuellement le rendu Direct3D.

Résultat v1.1.1 : **431 vérifications C#** et **11 vérifications du panneau réussies**. La régression du chargement simule une file initialement saturée à dix entrées puis une demande de quarante fichiers, une référence évincée, un fichier absent, une annulation et un timeout avec identification de la ressource. Le scan sur la scène réelle de l'utilisateur reste à confirmer dans l'application.

Résultat v1.1.2 : **442 vérifications C# réussies**, avec les deux cas de changement du nombre de géométries, relecture binaire et contrôle de préservation des sources. Le package réel `hw1_23_build1` doit être réexporté depuis l'application ; le fichier d'erreur seul ne contient pas les meshes ni les points de la zone permettant de rejouer cet export à l'identique.

Résultat v1.1.3 : **378 vérifications C# réussies** sur le nouveau mode (les HD ne passent plus dans les assertions de sommets découpés), dont références HD/ORPHANHD sans drawable chargé, ajout hors zone, conservation des transformations et des sources, absence de copies HD, couples binaire/XML, collisions de textures avec contenus différents, mutualisation des contenus identiques et liens matériaux après relecture binaire/XML. L'import Blender 5.0 + Sollumz, la résolution des originaux, leur placement, les trois collections et la réalisation après rotation sont également testés. La scène réelle reste à réexporter pour validation visuelle.

Résultat v1.1.4 : **392 vérifications C# réussies**. Les nouveaux tests reproduisent le refus Windows avec un handle de dossier réellement ouvert sans partage de suppression, puis couvrent sa libération, l'annulation, une destination existante et un verrou persistant pendant un export complet. Ils vérifient aussi la présence de l'importeur et des couples binaire/XML dans le dossier conservé.

Résultat v1.1.5 : **671 vérifications C# réussies** et **18 imports de collisions Blender 5.0 + Sollumz réussis**. Couverture : cinq types de bounds natifs, quatre primitives polygonales, conservation des types/matériaux et transformations non uniformes après relecture binaire/XML, coupes fermées, primitive contenant toute la zone, exclusion hors zone, contour concave, pièces YFT avec transformations de bones, dédoublonnage, flag d'entité désactivant les collisions et fixture YFT réelle Sollumz. Le bâtiment de l'utilisateur doit être réexporté et vérifié visuellement ; ces tests ne constituent pas un essai en jeu.

L'import/réalisation avait également été validé sous **Blender 5.0 + Sollumz** dans la v1, sans modification de ce chemin dans la v1.1. Le processus Blender se termine avec le code 0 ; la dépendance native `pymateria/nanobind` émet cependant des diagnostics de références non libérées à sa fermeture. L'audit NuGet des dépendances du projet de tests n'a pas pu joindre le service réseau ; les compilations réussissent.

Cela ne remplace pas un essai interactif sur un bâtiment réel. Le viewport de ce nouvel outil et le résultat en jeu restent à valider manuellement.

Depuis la racine du dépôt, dans PowerShell, utiliser MSBuild **64 bits** :

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe' CodeWalker\CodeWalker.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:UseSharedCompilation=false /p:OutDir=D:\GitHub\CodeWalker\CodeWalker\bin\ZoneEditor-v1.1.7\ /nr:false /v:minimal /nologo
```

Tests : projet console `CodeWalker.ZoneTests/CodeWalker.ZoneTests.csproj` ; son exécutable accepte un chemin de fixture YDR XML puis un dossier de sortie optionnel. Le test Blender est `tools/test_zone_blender.py`, à lancer avec `--background --factory-startup --python-exit-code 1 --python`, puis `-- <package> <dossier-addons>`. Les fixtures Sollumz existantes sont lues sur place et ne sont pas copiées dans les sources.

Pour les contrôles d'édition : `CodeWalker.ZoneTests.exe --editor <chemin-absolu-vers-CodeWalker.exe>`.

Pour relire un package existant sans le modifier : `CodeWalker.ZoneTests.exe --validate-package <chemin-absolu-du-package>`.
