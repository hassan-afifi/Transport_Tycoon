using UnityEngine;

public class VehicleAgent : MonoBehaviour
{
    [SerializeField] private int vehicleId;
    [SerializeField] private CargoType cargoType = CargoType.None;

    public int VehicleId => vehicleId;
    public CargoType CargoType => cargoType;

    public void Initialize(int id, CargoType type)
    {
        vehicleId = id;
        cargoType = type;
    }
}
