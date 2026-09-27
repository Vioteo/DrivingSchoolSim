using System;
using System.Linq;
using DrivingSchool.Editor;
using DrivingSchool.Presentation;
using DrivingSchool.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T50: every car of the catalogue meets the model contract, so the shared stack works on it.</summary>
    public sealed class VehicleCatalogTests
    {
        [Test]
        public void CatalogLoadsAndHasTwoPlayerCars()
        {
            var c = VehicleAssembler.LoadCatalog();
            Assert.That(c.Players().Select(v => v.id), Is.EquivalentTo(new[] { "DS_Sedan_A", "DS_Crossover_A" }));
            Assert.That(c.Traffic().Count(), Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void EveryCatalogueModelMeetsTheContract()
        {
            foreach (var v in VehicleAssembler.LoadCatalog().vehicles)
            {
                if (v.player)
                {
                    var r = VehicleAssembler.AuditToFile(v, traffic: false);
                    Assert.That(r.UsableForPlayer, Is.True, v.id + " as player car:\n" + r.ToMarkdown());
                    Assert.That(Mathf.Abs(r.WheelbaseM - v.spec.wheelbaseM), Is.LessThan(0.15f), v.id + ": spec wheelbase differs from the model");
                }
                if (v.traffic)
                {
                    var r = VehicleAssembler.AuditToFile(v, traffic: true);
                    Assert.That(r.UsableForTraffic, Is.True, v.id + " as traffic car:\n" + r.ToMarkdown());
                }
            }
        }

        [Test]
        public void TrafficPrefabsHaveAKinematicBodyAndLamps()
        {
            foreach (var v in VehicleAssembler.LoadCatalog().Traffic())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAssembler.TrafficPrefabPath(v));
                Assert.That(prefab, Is.Not.Null, v.id + ": run Driving School/Vehicles/Import, audit and build traffic prefabs");
                Assert.That(prefab.GetComponent<Rigidbody>().isKinematic, Is.True, v.id);
                Assert.That(prefab.GetComponent<TrafficVehicleView>(), Is.Not.Null, v.id);
                Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Count(r => r.name.EndsWith("_Glow")), Is.GreaterThanOrEqualTo(3), v.id + ": brake and turn lamp overlays");
                Assert.That(prefab.GetComponent<TrafficVehicleView>().LengthM, Is.InRange(3.5f, 6.5f), v.id);
            }
        }

        [Test]
        public void AModelWithoutWheelsIsRejected()
        {
            var car = new GameObject("car"); var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                body.transform.SetParent(car.transform, false); body.transform.localScale = new Vector3(1.8f, 1.4f, 4.4f); body.transform.localPosition = Vector3.up * 0.7f;
                var r = VehicleModelContract.Audit(car.transform, body.transform);
                Assert.That(r.UsableForTraffic, Is.False);
                Assert.That(r.Results.First(x => x.Feature.Id == "wheels").Ok, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(car); }
        }

        [Test]
        public void ABrokenSpecNamesTheCar()
        {
            var e = new VehicleEntry { id = "Broken", model = "x.fbx", player = true, spec = new VehicleSpec { massKg = 0 } };
            var ex = Assert.Throws<ArgumentException>(() => new VehicleCatalog { vehicles = new[] { e } }.Validate());
            StringAssert.Contains("Broken", ex.Message);
            Assert.Throws<ArgumentException>(() => new VehicleCatalog { vehicles = new[] { new VehicleEntry { id = "t", model = "x", traffic = true } } }.Validate(), "no player car");
        }
    }
}
