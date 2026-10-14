# Analyse : capture de zones GTA V vers des prefabs Blender

La proposition de workflow de ce document est remplacée par la [spécification de reconstruction d'un package](D:/GitHub/CodeWalker/docs/ragequit-package-rebuild.fr.md), qui intègre les ajouts manuels, les fichiers recentrés, les trois collections et les occlusions. Les observations sur le code restent utiles.

Analyse du 14 octobre 2026. Les observations ci-dessous proviennent des sources locales de CodeWalker, ragequit-blender, Sollumz 5.0/5.1 et GTA V. Les comportements décrits comme proposés ne sont pas encore implémentés. Aucun transfert réel vers Blender n'a été exécuté pour cette analyse.

**Recommandation : créer un prefab de zone, constitué d'une collection Blender complète, avec un pivot commun et un manifeste de provenance.** Le polygone sert à trouver les éléments candidats ; une prévisualisation permet de corriger leur appartenance au bâtiment. Il ne faut pas supposer que GTA possède une relation universelle « ce bâtiment possède ces props et ces collisions ».

1. **Ce qui existe déjà et ce qui manque**

| Élément | Constat dans le code local | Conséquence |
|---|---|---|
| Bridge CodeWalker | `RageQuitBridgePanel.BuildEntityJson` transmet nom, position et orientation ; sait distinguer le fichier YDD et un drawable demandé | Réutiliser le panneau et le transport HTTP |
| Identité du modèle | Le bridge emploie `ent.Name`, donc le nom d'archetype ; `Archetype.InitVars` et `TryGetDrawable` utilisent `assetName` lorsqu'il existe | Résoudre le véritable asset côté CodeWalker ; conserver séparément archetype, fichier et entrée YDD |
| Échelle | `YmapEntityDef.Scale` existe, mais n'est pas sérialisé ni appliqué par le bridge Blender | Ajouter XYZ, sans perdre la distinction scaleXY/scaleZ pour l'export GTA |
| YDD | Le champ `drawable` envoyé par CodeWalker est abandonné dans `CWHandler.do_POST` | L'import actuel ne filtre pas l'entrée du dictionnaire et peut positionner plusieurs drawables à la même transformation |
| Plusieurs occurrences | Les listes CodeWalker conservent les instances ; Blender relance recherche/téléchargement/import pour chaque entité | Importer une définition une fois, puis créer ses placements |
| Résolution des fichiers | `_download_asset` et `_download_ybn` prennent `paths[0]` | Le nom seul ne garantit pas la même version RPF/DLC que celle affichée dans CodeWalker |
| Collisions | Le bridge transmet des noms de YBN complets ; Blender les importe sans sélection interne | Ajouter le chemin des bounds retenus, leur espace de coordonnées et, si nécessaire, une sélection de primitives |
| Résultat HTTP | Le serveur répond OK après mise en file ; les imports se font ensuite | Distinguer « reçu », « importé », « incomplet » et « échoué » |
| Requête mixte | Le serveur traite `entities`, `ybns`, `ymaps` par branches `if/elif` | Une requête d'assemblage doit avoir son propre traitement, sinon certains groupes sont ignorés |

Sources principales :

- [Bridge CodeWalker](D:/GitHub/CodeWalker/CodeWalker/Project/Panels/RageQuitBridgePanel.cs:183).
- [Réception Blender](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/ragequit-blender/api/codewalker/http_server.py:53>).
- [Téléchargement, import et transformations](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/ragequit-blender/api/codewalker/operators.py:88>).
- [Résolution des drawables](D:/GitHub/CodeWalker/CodeWalker.Core/GameFiles/GameFileCache.cs:2681).

2. **Sélection dans CodeWalker**

Proposition d'interaction : « Capturer une zone », clics pour les sommets, Retour arrière pour retirer le dernier, Entrée pour fermer, Échap pour annuler. Autoriser le déplacement des sommets après fermeture. Utiliser une vue du dessus ou un plan horizontal choisi, avec accrochage au sol facultatif. Une projection sur le toit ne doit pas modifier implicitement la hauteur de référence de toute la zone.

La sélection représente un polygone XY, éventuellement concave, extrudé entre Z min et Z max. La hauteur est indispensable pour distinguer bâtiment, tunnel, viaduc et sous-sol. Rejeter les polygones dégénérés ou auto-intersectés. Le contour sert à sélectionner des objets entiers ; il ne découpe pas les meshes visuels par défaut.

Le pipeline proposé :

1. Calculer la boîte englobante de la zone.
2. Interroger `Space.MapDataStore.GetItems(min,max)` et `Space.BoundsStore.GetItems(min,max)`.
3. Copier les résultats sous synchronisation : ces méthodes réutilisent une liste `VisibleItems`, également utilisée par le rendu. Ne pas lancer des requêtes concurrentes sur cette liste partagée.
4. Charger explicitement les YMAP/YBN candidats, résoudre les archetypes et intégrer les fichiers du projet, y compris les modifications non enregistrées.
5. Attendre les ressources avec progression, annulation et liste des éléments manquants. Un fichier encore en chargement n'est pas une zone vide.
6. Filtrer précisément le polygone et la hauteur. Le store YMAP fait un test XY : il ne remplace pas le filtre Z.
7. Afficher les résultats, avec ajout/retrait manuel et mise en évidence des objets qui débordent.

Ne pas utiliser uniquement `Renderer.VisibleYmaps` ou les entités rendues : cela dépend de la caméra, du streaming et du LOD courant. La capture doit être stable après déplacement de caméra. Prévoir un choix explicite entre l'état actif du monde et les variantes disponibles ; ne pas mélanger automatiquement des variantes mutuellement exclusives de DLC, météo ou horaire.

Pour les props, proposer le pivot dans la zone et l'intersection de l'emprise comme deux modes. Pour les bâtiments, l'intersection de l'emprise est préférable au pivot seul, car celui-ci peut être loin de la géométrie. La boîte orientée projetée reste une approximation : les cas qui touchent les bords demandent une prévisualisation ou un test géométrique plus fin.

Points d'intégration existants : [événements souris](D:/GitHub/CodeWalker/CodeWalker/WorldForm.cs:6325), [raycast du curseur](D:/GitHub/CodeWalker/CodeWalker/WorldForm.cs:2443), [store YMAP](D:/GitHub/CodeWalker/CodeWalker.Core/World/Space.cs:1781), [store collisions](D:/GitHub/CodeWalker/CodeWalker.Core/World/Space.cs:1989). Le dessin du contour peut utiliser les primitives de sélection déjà présentes dans `WorldForm` et le renderer.

3. **Conserver les bonnes hiérarchies**

Il faut distinguer trois relations : la hiérarchie interne d'un drawable, les liens LOD entre entités de carte, et les entités d'un intérieur MLO.

Pour un extérieur, importer par défaut les entités HD et ORPHANHD. Conserver les références LOD/SLOD comme métadonnées et variantes facultatives, sans les superposer aux modèles détaillés. Un parent LOD peut agréger plusieurs bâtiments ; il ne définit donc pas l'appartenance à un prefab. Les modèles HIGH/MEDIUM/LOW internes au YDR constituent encore un autre niveau de détail à conserver via Sollumz.

Le source GTA le confirme explicitement : `fwscene/mapdata/mapdata.psc` regroupe parentIndex, lodDist, childLodDist, lodLevel et numChildren comme données de LOD ; `mapdatacontents.cpp` attache ces entités à leur LOD, dans le même YMAP ou dans le parent.

Pour les MLO, prévoir une option distincte « intérieur complet » : transformation de l'instance, entités internes, rooms, portals, entity sets, collisions locales et métadonnées. Il existe déjà `gtav/interior/ytyp/import_mlo.py` dans ragequit-blender. Aplatir uniquement les meshes ne restituerait pas un MLO éditable et réexportable. Ne pas activer tous les entity sets simultanément.

4. **Lumières et collisions**

Les lumières ont plusieurs sources : attributs du drawable, extensions d'entités/archetypes, et tableaux LOD/distant LOD du YMAP. Elles doivent garder leur type et leur provenance. Une lumière LOD n'est pas automatiquement une nouvelle lampe à ajouter à côté de la lumière détaillée. Les emissives sont encore un élément distinct.

Dans les versions locales de Sollumz, `create_drawable` crée les lumières et les collisions intégrées comme objets de la hiérarchie. Le mode `create_drawable_as_asset` fusionne les modèles HIGH, retire les autres LOD, sérialise les lumières et conserve seulement la boîte min/max de la collision dans `sz_asset_collisions`. Il ne conserve donc pas la géométrie de collision complète dans cette propriété. Le patch RageQuit de conversion fusionne également les modèles et supprime les autres objets de la hiérarchie qu'il reçoit.

**Pour cette fonctionnalité, importer avec `import_as_asset=False`, puis marquer la collection finale comme asset Blender.** L'asset de collection et le mode d'import « as asset » de Sollumz ont des objectifs différents.

Sources : [import hiérarchique Sollumz](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/Sollumz/ydr/ydrimport.py:75>), [import simplifié](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/Sollumz/ydr/ydrimport.py:575>), [patch RageQuit](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/ragequit-blender/addon_patcher/sollumz_convert_object_to_asset_patch.py:19>).

Pour les collisions :

- Intégrées au drawable : conserver la géométrie, les matériaux, les flags et la transformation de l'entité.
- YBN du monde : sélectionner spatialement les enfants de `BoundComposite`, avec leurs transformations et flags. Garder les enfants entièrement inclus ; mettre en évidence ceux qui chevauchent la limite.
- Grands enfants de collision : offrir ensuite une sélection de triangles/primitives. Tester l'intersection réelle avec le prisme ; un test du centre des triangles laisse des trous. Conserver les primitives entières qui intersectent peut déborder ; le découpage exact est une fonctionnalité supplémentaire avec reconstruction des données de collision.
- MLO : appliquer la transformation de l'instance au YBN local. CodeWalker possède déjà ce traitement dans `RayIntersectInterior`.

Un sous-bound n'est pas nécessairement un bâtiment. Un même ensemble peut contenir façade, sol et voisinage. La sélection automatique doit donc rester corrigeable. La comparaison des noms de YDR/YBN ne suffit pas, pas plus qu'une conversion de la seule bounding box en collision.

Sources : [structures des bounds](D:/GitHub/CodeWalker/CodeWalker.Core/GameFiles/Resources/Bounds.cs:2822), [collisions MLO](D:/GitHub/CodeWalker/CodeWalker.Core/World/Space.cs:1410), [store GTA](<F:/#Logiciel/Vacances Barcelone/GTAVSP/GTAV Source/src/dev_ng/rage/framework/src/fwscene/stores/staticboundsstore.h:155>). Les schemas GTA `maptypes.psc` distinguent assetName, drawableDictionary, textureDictionary et physicsDictionary ; les fichiers `MapData_Extensions.psc` et `MapData.psc` distinguent aussi les sources de lumières.

5. **Le prefab dans Blender et son pivot**

Une seule collection principale marquée comme asset est recommandée. Elle contient les hiérarchies complètes des bâtiments et props, les lumières, et les collisions du monde retenues. Les collisions intégrées restent associées à leur drawable ; l'organisation en collections ne doit pas casser les relations de parentage. Des propriétés de rôle permettent aux outils de retrouver et masquer les collisions sans se baser sur le nom.

L'artiste glisse un seul asset. La collection est instanciée ; aucune fusion des meshes n'est nécessaire. Un opérateur « Rendre le prefab éditable » crée les objets, restaure les parents et applique la transformation du placement. Les datablocks de meshes et matériaux restent partagés par défaut ; l'option « copie indépendante » les duplique pour une modification locale. Il doit gérer les instances imbriquées, les bibliothèques liées et les références de contraintes/modificateurs, pas uniquement appeler une conversion sur un seul niveau.

Choisir un pivot explicite, idéalement au pied du bâtiment, puis convertir les transformations une seule fois : `M_local = inverse(M_pivot) * M_source_world`. Au placement : `M_world = M_instance * M_local`. Appliquer ce changement de repère aux racines importées en préservant leurs enfants. Les lumières d'extension et collisions du monde doivent subir le même changement de repère. Stocker l'origine GTA dans le manifeste ; ne pas additionner une seconde fois un `collection.instance_offset` déjà compensé.

Deux instances indépendantes, visuel et collision, sont possibles mais risquent de se désynchroniser. Si la séparation devient nécessaire, un seul contrôle de placement doit piloter les deux, avec le même identifiant et la même transformation. La collection principale unique est plus simple pour le premier usage.

Attention aux rotations : CodeWalker inverse déjà la rotation brute des `CEntityDef` pour obtenir `YmapEntityDef.Orientation`, tandis que les `CMloInstanceDef` suivent une convention différente. Le bridge transmet actuellement cette orientation résolue. Ne pas lui appliquer à nouveau l'inversion de l'import XML.

Réemploi utile : [copie d'une hiérarchie avec données partagées](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/ragequit-blender/gtav/interior/ymap/importer.py:86>). Le [pipeline de prefabs existant](<C:/Users/Perrin/AppData/Roaming/Blender Foundation/Blender/5.0/scripts/addons/ragequit-blender/gtav/placement/buildings/prefab_adapter.py:425>) sélectionne les meshes pilotés par le modifier de bâtiment et fusionne les variantes : il faut une voie d'import/conversion dédiée aux assemblages GTA. La réexportation ultérieure devra recalculer les indices parentIndex et les groupes de streaming pour la nouvelle ville, pas recopier ceux de la carte d'origine.

6. **Transport et dépendances proposés**

Conserver le bridge HTTP existant et ajouter une requête versionnée `assembly`. Le manifeste décrit :

- version du schéma, identifiant de job/prefab, nom, provenance du jeu et configuration DLC ;
- polygone, plage Z, pivot, règles de sélection, heure/variantes retenues ;
- définitions d'assets uniques : type, chemin source exact, hash, entrée YDD, textures et dépendances ;
- occurrences : identifiant stable fondé sur source YMAP + index, archetype, transformation complète, flags et métadonnées ;
- collisions : source YBN, chemin d'enfant, primitives retenues si nécessaire, repère et transformation ;
- lumières/extensions, avertissements et ressources manquantes.

Dédupliquer les ressources par identité de fichier/contenu et les occurrences par identité de placement, jamais les deux par le seul nom du modèle. Conserver un hash de la source pour vérifier que les indices de collision correspondent toujours au bon fichier.

Pour fiabiliser la capture, privilégier à terme un paquet local produit depuis les ressources effectivement résolues par CodeWalker : manifeste + assets + textures. Les fichiers de projet modifiés doivent être sérialisés depuis leur état courant. CodeWalker a déjà les lecteurs RPF et les exports XML YDR/YBN ; choisir le format accepté par la version de Sollumz réellement utilisée. Le transport peut transmettre le chemin du paquet local, puis Blender accuse réception et fournit l'état du job.

Le chemin API existant peut rester disponible, mais il doit résoudre un fichier exact et vérifier la correspondance de version, au lieu de sélectionner le premier résultat. Les workers actuels demandent `textures=False` ; une bibliothèque autonome exige de résoudre les YTD/parents nécessaires, puis de conserver les textures dans la bibliothèque ou de les empaqueter. Ne pas laisser des références vers les répertoires temporaires supprimés par l'importeur.

Le serveur HTTP met en file une commande validée ; les téléchargements restent hors du thread Blender et toute manipulation de `bpy` reste sur le thread principal. Prévoir progression, annulation, reprise et rapport d'import. Le manifeste sert aussi à refaire une capture et à retrouver les sources d'un élément.

7. **Ordre d'implémentation proposé et validation**

Premier lot : outil polygone + hauteur, interrogation spatiale complète, prévisualisation, choix manuel des entités, HD/ORPHANHD par défaut. Séparer le service de recherche dans Core du panneau et des événements souris.

Deuxième lot : protocole assembly, identité exacte des modèles et entrées YDD, échelles, import hiérarchique, cache des définitions, pivot commun, collection asset et réalisation éditable. Inclure les collisions intégrées et les lumières des drawables.

Troisième lot : extraction des collisions du monde par sous-bounds, aperçu des débordements puis sélection plus fine. Ce lot est nécessaire avant de considérer l'objectif « bâtiment complet avec collisions du monde » comme atteint.

Quatrième lot : extensions et lumières de carte, variantes horaires, MLO complets et intégration avancée à l'export de la nouvelle ville. Tout type non traité doit être signalé dès les premiers lots plutôt qu'ignoré silencieusement.

Cas de validation indispensables : même capture caméra éloignée/proche ; ressource non encore chargée ; prop au pivot extérieur ; polygone concave ; tunnel sous bâtiment ; échelle non unitaire ; archetype différent d'assetName ; deux entrées d'un YDD ; plusieurs occurrences d'un même modèle ; YBN traversant la zone ; collision locale d'un MLO ; lumière parentée ; rotation du prefab de 90 degrés ; réalisation de deux placements sans modifier la définition source ; réouverture du .blend sans fichiers temporaires ; distinction entre HTTP reçu et import terminé.

Le projet principal cible .NET Framework 4.8 et la configuration Release/AnyCPU fixe PlatformTarget=x64. MSBuild Visual Studio 18 Professional est installé. Commande du build de référence :

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe' CodeWalker\CodeWalker.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:UseSharedCompilation=false /nr:false /v:minimal /nologo
```

Résultat : build terminé avec code de sortie 0, sans avertissement ni erreur affiché. Exécutable : `D:\GitHub\CodeWalker\CodeWalker\bin\Release\net48\CodeWalker.exe`. Le premier essai avec MSBuild du dossier Bin restait à l'étape CoreResGen ; il a été interrompu. L'exécutable MSBuild du sous-dossier amd64 a terminé le build. Aucun nouveau comportement de capture n'est inclus et aucun test interactif de CodeWalker/Blender n'a été effectué.
