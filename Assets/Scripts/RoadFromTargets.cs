using System;
using UnityEngine;

// Compat wrapper: permite añadir el componente por el nombre histórico "RoadFromTargets"
// Delegando toda la lógica a RoadFromTargetsSticky (que está dividido en parciales bajo Assets/Scripts/carreteras)
[AddComponentMenu("Carreteras/Road From Targets (Compat)")]
public class RoadFromTargets : RoadFromTargetsSticky { }