# Spécification proposée : reconstruire un package de bâtiment dans CodeWalker

Mise à jour du 14 octobre 2026 : reconstruction côté CodeWalker, occlusions dans un YMAP séparé et découpe obligatoire de tout ce qui dépasse de la zone dans les nouvelles ressources exportées. Ce document remplace la proposition de transfert d'assemblage brut de l'analyse initiale. Il décrit la cible complète ; la première implémentation expérimentale et ses limites sont détaillées dans [la notice v1](zone-package-v1.fr.md).

**CodeWalker prépare et exporte un package autonome recentré. Blender importe ce package et crée trois collections instanciables.** Les sources GTA restent intactes : la reconstruction travaille sur des copies.

1. **Sélection de la zone et corrections manuelles**

Dessiner un polygone XY et régler Z min/Z max. Rechercher les entités, collisions, lumières et occlusions sans dépendre uniquement de ce que rend la caméra. Le test d'intersection de l'emprise doit pouvoir retenir un bâtiment dont le pivot est extérieur.

Ajouter les commandes « Ajouter la sélection » et « Retirer la sélection ». Un ajout manuel reste dans la liste des sources même si son pivot est hors zone. Il s'agit d'une occurrence placée avec sa transformation, pas simplement d'un nom de fichier YDR. Plusieurs occurrences d'un même drawable restent distinctes. Une modification de la zone ne doit pas annuler les inclusions/exclusions manuelles. L'ajout manuel ne dispense pas de découpe : seule la partie géométrique à l'intérieur du volume est exportée. Une source entièrement extérieure ne produit aucune géométrie et apparaît comme telle dans le bilan.

Les sources candidates sont : (résultats de zone UNION ajouts manuels) MOINS exclusions manuelles. Le résultat exporté est ensuite leur intersection géométrique avec le volume de capture. Inclure les dépendances propres de chaque entité retenue et ses sources LOD ; un agrégat est découpé avant export, jamais conservé entier s'il dépasse. L'ajout d'une entité ne doit pas étendre silencieusement la zone de capture des collisions du monde ou des occlusions ; celles-ci peuvent être ajoutées manuellement également.

2. **Nom et pivot**

La commande « Utiliser la sélection comme pivot » désigne une seule occurrence, normalement le bâtiment principal. Son nom propose le nom du package. Un champ permet un nom personnalisé ; conserver un nom d'affichage et un identifiant de fichier compatible GTA, avec contrôle des doublons.

Par défaut, déplacer le pivot à (0,0,0) par translation uniquement, en conservant les axes du monde. L'option distincte « Aligner sur l'orientation du bâtiment » applique une rotation inverse autour de Z. Ne pas incorporer l'échelle du bâtiment au repère du package. L'orientation complète avec inclinaison nécessite un traitement supplémentaire des box occluders, qui n'encodent pas une orientation 3D arbitraire.

Pour une matrice de repère P, toutes les données partagent la conversion M_local = inverse(P) * M_world. Les racines des collections/instances sont à zéro, mais les props et autres éléments gardent leurs positions relatives ; mettre tous les objets individuellement à zéro détruirait l'assemblage.

3. **Contenu du package**

Exemple pour un nom `building_01` :

```text
building_01/
  building_01.ymap
  building_01_occl.ymap
  building_01.ytyp
  building_01.ybn
  building_01_lod.ydr
  building_01_slod1.ydr
  building_01_slod2.ydr
  ... autres niveaux présents et retenus
  assets/       modèles HD, props et dépendances
  textures/     textures/dictionnaires nécessaires
  manifest.json
```

L'arborescence est celle d'un package d'import ; elle n'est pas en soi une ressource FiveM prête à déployer. Un éventuel export jeu devra aussi respecter l'organisation de streaming et les règles de noms.

`building_01.ymap` décrit les entités recentrées, leurs liens LOD reconstruits et les lumières de carte retenues ; il ne contient pas d'occlusions. `building_01_occl.ymap` contient exclusivement les données d'occlusion de la capture, avec leurs propres extents et flags recalculés. Les deux utilisent exactement le même repère. Le YTYP fournit les définitions des modèles renommés ou créés, avec le bon assetType, les bounds et les dépendances de textures. Il permet de résoudre les nouveaux YDR LOD autonomes. Les métadonnées des archetypes HD/props nécessaires au package doivent également être conservées. Le YBN rassemble les collisions du monde découpées dans le repère du package.

Les modèles HD et les props restent distincts afin de conserver leurs hiérarchies et leur éditabilité. Leurs transformations relatives sont portées par le YMAP. Les modèles entièrement intérieurs peuvent conserver leur géométrie ; ceux qui traversent la frontière donnent de nouvelles variantes découpées, avec des identifiants propres et des références YMAP/YTYP mises à jour. Deux occurrences du même modèle peuvent nécessiter des variantes différentes, car la coupe dépend de leur placement. Les ressources et entrées YDD sont résolues précisément depuis les assets utilisés par CodeWalker, sans recherche approximative par nom.

Le manifeste stocke le pivot d'origine, les chemins/hashes sources, les occurrences, les choix de sélection, les rôles des fichiers, les niveaux LOD et les éléments manquants. Il permet de restaurer le placement GTA d'origine ou de reconstruire le package.

4. **Extraction et reconstruction des LOD**

Objectif : un YDR autonome par niveau retenu, nommé `<nom>_lod`, `<nom>_slod1`, etc. Ce sont les niveaux d'entités de la carte ; ils ne doivent pas être confondus avec les modèles High/Med/Low internes à un drawable.

Si un niveau correspond à une seule entrée YDD adaptée au bâtiment, copier cette entrée, conserver shaders/textures et reconstruire un YDR indépendant. Pour garantir son pivot à zéro, appliquer la transformation du placement LOD vers le repère du package à sa géométrie et aux données associées. La simple extraction d'une entrée YDD conserve son repère original et ne suffit pas toujours.

Si plusieurs drawables participent au même niveau, produire un seul drawable avec plusieurs modèles/géométries : transformer les positions, normales et tangentes correctement, reconstruire les références de shaders/matériaux et recalculer les bounds. Il ne s'agit pas d'une simple concaténation de fichiers. Les données partagées du cache CodeWalker doivent être copiées profondément avant modification ; `DrawableBase.ShallowCopy()` partage encore les tableaux de modèles.

Un SLOD peut contenir un quartier entier sans association par triangle au bâtiment sélectionné. Il est découpé au volume de capture comme les autres sources. Cela supprime sa partie extérieure, mais ne transforme pas une approximation de quartier en LOD fidèle d'un bâtiment : les parties restantes peuvent demander une retouche, en particulier si la géométrie simplifiée n'épouse pas le HD. Un niveau absent reste absent, sauf demande explicite de génération/simplification ; ne pas créer un faux SLOD en renommant le HD.

Recalculer les liens parentIndex/numChildren, distances, flags et extents du nouvel ensemble. CodeWalker sait représenter des parents LOD dans le même YMAP ou un YMAP parent ; la stratégie de sortie doit être explicite et testée. Pour le package Blender, le manifeste décrit également les niveaux pour les ranger séparément sans dépendre du rendu LOD courant de CodeWalker.

5. **Collisions et occlusions**

Conserver l'association des collisions intégrées aux drawables et découper leur géométrie au même volume. Le YBN reconstruit contient les collisions du monde découpées ; ne pas y recopier automatiquement une seconde fois les collisions intégrées. Lors de l'import Blender, les deux sources sont présentées dans la collection Collision en préservant les liens et les transformations nécessaires. Pour un futur export qui centraliserait absolument toutes les collisions dans le YBN, prévoir une conversion explicite des modèles copiés afin d'éviter des doublons en jeu.

Pour les YBN du monde, rechercher les sous-bounds et découper les triangles/primitives traversant la frontière. Conserver matériaux, flags et transformations, puis reconstruire les données dérivées nécessaires, dont bounds/BVH. Les primitives analytiques partiellement coupées peuvent nécessiter une conversion en géométrie de collision avec une tolérance définie ; ne pas prétendre représenter exactement une sphère tronquée par une simple nouvelle sphère ou boîte.

Inclure les deux types d'occlusion que CodeWalker lit et écrit :

- `boxOccluders` : boîtes d'occlusion, avec centre, taille et orientation ;
- `occludeModels` : modèles d'occlusion triangulés, avec vertices, indices et flags.

Ces données résident exclusivement dans `<nom>_occl.ymap`, pas dans le YBN ni dans le YMAP d'entités. Les chercher aussi dans les YMAP dédiés aux occlusions, même sans entités HD. Les découper et les recentrer avec le même repère, reconstruire leur encodage et leurs limites. Les indices des modèles d'occlusion sont des octets : un regroupement doit respecter cette limite et scinder les modèles si nécessaire. Conserver le sens des triangles ; un occluder ne doit pas devenir visible au rendu.

Un occluder partagé est découpé, pas simplement conservé ou rejeté en bloc. Une boîte dont l'intersection reste représentable comme box occluder peut conserver ce type ; sinon convertir les surfaces retenues en occlude models triangulés. Ne pas remplacer la forme découpée par sa boîte englobante, qui remplirait les parties retirées. Ne pas créer automatiquement de faces de fermeture d'occlusion sur les plans de coupe : elles pourraient masquer des ouvertures. Vérifier les faces conservées contre la géométrie visuelle exportée. Les occlusions restent éditables.

La coupe est une intersection avec le prisme du polygone XY et ses limites Z. Les triangles traversants sont scindés à la frontière ; ni un test de leur centre ni la suppression des seuls triangles entièrement extérieurs ne suffit. Pour un polygone concave, utiliser une méthode adaptée, par exemple une décomposition en cellules convexes avec gestion des frontières communes. Interpoler correctement les attributs des nouveaux vertices (UV, couleurs, normales, tangentes et poids si présents), conserver les matériaux et éliminer les éléments dégénérés. Les géométries animées/skinnées et les types non pris en charge doivent être signalés explicitement, jamais exportés intacts hors zone comme solution de repli.

Par défaut, la découpe retire la partie extérieure sans reboucher les meshes visuels. La fermeture éventuelle des surfaces de collision est une option distincte : fermer automatiquement toutes les coupes peut créer des murs invisibles aux jonctions entre assets. Le volume de capture et les sources sont transformés dans le même repère pour éviter tout décalage entre HD, LOD, collisions et occlusions.

Le module RageQuit `gtav/placement/buildings/occluders.py` fournit déjà un export de meshes `_occl` vers des YMAP d'occlusion. C'est une base de réemploi pour l'export ultérieur ; l'import et la conservation fidèle des deux types demandent une prise en charge dédiée.

6. **Les trois collections Blender**

```text
building_01_base
  bâtiment, props et hiérarchies visuelles
  lumières détaillées / extensions prises en charge
building_01_collision
  collisions intégrées et collisions du monde, sans doublons
  Occlusions/
    box occluders et occlude models, identifiés comme occlusion
building_01_lod
  LOD/
  SLOD1/
  SLOD2/
  ...
```

Le rangement des occlusions comme sous-collection technique de Collision est proposé pour conserver exactement trois collections principales. Le rôle `occlusion` reste distinct du rôle `collision` pour l'import, la visibilité et l'export. Elles sont désactivées au rendu et accessibles en aperçu technique. Un changement vers une quatrième collection pourra rester possible sans changer le format du package.

Les trois collections ont le même pivot et un instance_offset cohérent, sans double compensation. Un placement de bâtiment crée trois instances sous un seul Empty de contrôle. À la création, le contrôle et les transformations locales des trois instances sont à l'identité ; ensuite, le contrôle déplace tout ensemble. Les définitions sources ne doivent pas être affichées une seconde fois à côté des instances.

Les LOD sont organisés par niveau et ne s'affichent pas tous superposés par défaut. Une commande choisit le niveau à prévisualiser. Les lumières LOD restent identifiées séparément des lumières détaillées pour éviter un double éclairage.

L'opérateur « Rendre éditable » réalise les trois branches, conserve les catégories, les relations de parentage et les métadonnées. Partager les meshes par défaut et proposer une copie indépendante. Les références de contraintes et de modificateurs doivent être remappées lorsque les objets concernés sont copiés.

7. **Validation avant livraison**

Tester un bâtiment au pivot extérieur dont la géométrie entre dans la zone et une entité ajoutée entièrement hors zone (résultat vide signalé) ; plusieurs occurrences du même prop coupées différemment ; un nom d'archetype différent du fichier modèle ; une entrée précise de YDD ; un LOD composé de plusieurs drawables ; un SLOD partagé avec le voisinage ; des ressources absentes ; des collisions intégrées et du monde ; les deux types d'occluders ; un occluder traversant la zone ; des lumières parentées ; des échelles non unitaires. Pour les lumières et autres données ponctuelles, retirer les sources situées hors du volume ; leur rayon d'influence ne constitue pas une géométrie à découper.

Ajouter des tests de coupe sur polygone concave, frontières Z, triangles qui traversent la zone sans aucun sommet intérieur, faces coplanaires, UV aux nouveaux vertices et boîte d'occlusion devenant une forme non rectangulaire. Vérifier qu'aucune géométrie exportée ne dépasse du volume au-delà d'une tolérance numérique définie et que les deux YMAP séparent strictement entités et occlusions. Ne pas imposer que les pivots des entités soient dans le volume : c'est la géométrie transformée qui doit respecter la limite.

Recharger les YMAP/YTYP/YBN/YDR produits avec les lecteurs CodeWalker avant import Blender. Comparer positions et emprises après recentrage, vérifier les shaders/textures, les liens LOD et l'absence de mutations des ressources sources. Dans Blender, comparer les trois branches après déplacement/rotation du contrôle puis réalisation. Vérifier que l'occlusion ne masque pas des ouvertures et que le .blend se rouvre sans dépendre des fichiers temporaires.

Le build de référence de l'analyse précédente reste valide pour le code actuel. Cette mise à jour est documentaire et n'ajoute pas encore l'outil au binaire.
