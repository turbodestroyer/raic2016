//#define LOCAL

using Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk
{

    public class Vector2
    {
        public double x;
        public double y;

        public Vector2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public Vector2(LivingUnit unit)
        {
            this.x = unit.X;
            this.y = unit.Y;
        }

        public static Vector2 operator /(Vector2 v, double d)
        {
            return new Vector2(v.x / d, v.y / d);
        }

        public static Vector2 operator *(Vector2 v, double d)
        {
            return new Vector2(v.x * d, v.y * d);
        }

        public double Length()
        {
            return Math.Sqrt(Math.Pow(this.x, 2) + Math.Pow(this.y, 2));
        }
    }

    public class BonusInfo
    {
        public int X;
        public int Y;
    }

    public class BuildingInfo
    {
        public int hpCount;
        public double xPos;
        public double yPos;
        public double attackRange;
        public double radius;
    }

    public class UnitUnfo
    {
        public double xPos;
        public double yPos;
    }

    public sealed class MyStrategy : IStrategy
    {

        public MyStrategy()
        {
#if LOCAL
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
#endif
        }

#if LOCAL
        private VisualClient vc = new VisualClient("127.0.0.1", 13579);
#endif

        Dictionary<LaneType, List<Vector2>> waypointsByLane = new Dictionary<LaneType, List<Vector2>>();
        LaneType currentLaneType;

        Wizard wizard;
        World world;
        Game game;
        Move move;

        private const double fromDegreesToRadians = 0.0174533;
        private readonly double _maxAdditionalCastRange = 100; // from table
        private readonly Dictionary<int, BuildingInfo> enemyBuildings =
            new Dictionary<int, BuildingInfo>();
        private readonly Dictionary<long, UnitUnfo> enemyUnits =
            new Dictionary<long, UnitUnfo>();
        private List<BonusInfo> bonuses = new List<BonusInfo>();
        private List<BonusInfo> bonusesInAdvance = new List<BonusInfo>();

        private Random rand;

        private double prevX = 0.0, prevY = 0.0;
        private int waitTick = 0;
        private bool crazyMode = false;

        private int lastTick = -1;
        private int deathNumber = 0;
        
        public void Move(Wizard self, World world, Game game, Move move)
        {
#if LOCAL
            vc.BeginPost();
#endif

            this.wizard = self;
            this.world = world;
            this.game = game;
            this.move = move;

            if (world.TickIndex == 0)
                FirstTick();

            if (IsLowHp() || WoodcuttersBeside() || IAmAtTheForefrontAndUnderFire())
            {
                Abandon();
                FireIfCan();
            }
            else
            {
                if (GetNearestTargetThatICanShootCount() > 0)
                {
                    Fire();
                    if (ShouldGoToBonus())
                        GoToNearestBonus(false);
                }
                else
                {
                    if (ShouldGoToBonus())
                        GoToNearestBonus(true);
                    else
                    {
                        RotateTo(GetNextWaypoint());
                        Walk(GetNextWaypoint(), true);
                    }
                }
            }

            CheckStopping();
            TickEnd();
#if LOCAL
            vc.EndPost();
#endif
        }

        private bool IsLowHp()
        {
            int dangerousHpLevel = 50 + deathNumber * 4;
			  
																							  
																   
															  
																						   
			  
							   
																										
																			   
												  
			  
																				
						   

									  
            if (game.IsSkillsEnabled) dangerousHpLevel += 10;

            if (world.Wizards
                .Where(x => wizard.GetDistanceTo(x) <= game.WizardCastRange + _maxAdditionalCastRange)
                .Where(x => x.Faction != wizard.Faction).ToList().Count >= 2)
                    dangerousHpLevel += 5;
            
            if (wizard.Life < dangerousHpLevel) return true;
            return false;
        }

        private void FirstTick()
        {
            rand = new Random();
            InitializeWaypointsList();
            UpdateLane();
            InitializeBuildingsList();
        }

        private void CheckSkills()
        {
            if (!game.IsSkillsEnabled) return;
            if (wizard.Skills.Count() == wizard.Level) return;

            List<SkillType> orderToLearn = new List<SkillType>();
            orderToLearn.Add(SkillType.MagicalDamageBonusPassive1);
            orderToLearn.Add(SkillType.MagicalDamageBonusAura1);
            orderToLearn.Add(SkillType.MagicalDamageBonusPassive2);
            orderToLearn.Add(SkillType.MagicalDamageBonusAura2);
            orderToLearn.Add(SkillType.FrostBolt);

            orderToLearn.Add(SkillType.MagicalDamageAbsorptionPassive1);
            orderToLearn.Add(SkillType.MagicalDamageAbsorptionAura1);
            orderToLearn.Add(SkillType.MagicalDamageAbsorptionPassive2);
            orderToLearn.Add(SkillType.MagicalDamageAbsorptionAura2);

            orderToLearn.Add(SkillType.RangeBonusAura1);
            orderToLearn.Add(SkillType.RangeBonusPassive1);
            orderToLearn.Add(SkillType.RangeBonusAura2);
            orderToLearn.Add(SkillType.RangeBonusPassive2);

            if (orderToLearn.Any(x => !wizard.Skills.Contains(x)))
                move.SkillToLearn = orderToLearn.Where(x => !wizard.Skills.Contains(x)).FirstOrDefault();
        }

        private void Abandon()
        {
            if (WoodcuttersBeside()
                ||
                GetNearestTargetThatCanShootMeCount() > 0)
            {
                Vector2 prevWaypoint = GetPreviousWaypoint();
                Walk(prevWaypoint);
            }
            else
            {
                move.Turn = wizard.GetAngleTo(GetNextWaypoint().x, GetNextWaypoint().y);
            }
        }

        private void InitializeWaypointsList()
        {
            double mapSize = game.MapSize;

            int rnd1 = rand.Next();
            int rnd2 = rand.Next();
            int rnd3 = rand.Next();

            double additionalEdge = rnd3 % 2 == 0 ? 40.0 : -40.0;
            double additionalCenter = rnd3 % 2 == 0 ? 100.0 : -100.0;

            waypointsByLane.Add(LaneType.Middle, new List<Vector2>() {
                rnd1 % 2 == 0 ? new Vector2(400, mapSize-200) : new Vector2(200, mapSize-400),
                rnd1 % 2 == 0 ? new Vector2(600, mapSize-200) : new Vector2(200, mapSize-600),
                rnd1 % 2 == 0 ? new Vector2(600, mapSize-400) : new Vector2(400, mapSize-600),
                rnd1 % 2 == 0 ? new Vector2(800, mapSize-600) : new Vector2(600, mapSize-800),

                //new Vector2(700, mapSize - 700),
                new Vector2(900 + additionalCenter, mapSize - 900 + additionalCenter),
                new Vector2(1200 + additionalCenter, mapSize - 1200 + additionalCenter),
                new Vector2(1500 + Math.Abs(additionalCenter), mapSize - 1500 + Math.Abs(additionalCenter)),
                new Vector2(1750 + Math.Abs(additionalCenter), mapSize - 1750 + Math.Abs(additionalCenter)),

                new Vector2(mapSize/2 + Math.Abs(additionalCenter), mapSize/2 + Math.Abs(additionalCenter)), //s
                
                new Vector2(mapSize - 1750 + additionalCenter, 1750 + additionalCenter),
                new Vector2(mapSize - 1500 + additionalCenter, 1500 + additionalCenter),
                new Vector2(mapSize - 1200 + Math.Abs(additionalCenter), 1200 + Math.Abs(additionalCenter)),
                new Vector2(mapSize - 900 + Math.Abs(additionalCenter), 900 + Math.Abs(additionalCenter)),
                new Vector2(mapSize - 700 + Math.Abs(additionalCenter), 700 + Math.Abs(additionalCenter)),

                rnd2 % 2 == 0 ? new Vector2(mapSize-600, 400) : new Vector2(mapSize-400, 600),
                rnd2 % 2 == 0 ? new Vector2(mapSize-400, 200) : new Vector2(mapSize-200, 400)
            });

            waypointsByLane.Add(LaneType.Top, new List<Vector2>() {
                new Vector2(150 + additionalEdge, mapSize - 100),
                new Vector2(150 + additionalEdge, mapSize - 500),
                new Vector2(200 + additionalEdge, mapSize - 1000),
                new Vector2(200 + additionalEdge, mapSize - 1500),
                new Vector2(200 + additionalEdge, mapSize * 0.5),
                new Vector2(200 + additionalEdge, 1500),
                new Vector2(200 + additionalEdge, 1000),

                new Vector2(200 + additionalEdge, 600),
                new Vector2(400 + additionalEdge, 400 + additionalEdge), //s
                new Vector2(600, 200 + additionalEdge),

                new Vector2(1000, 200 + additionalEdge),
                new Vector2(1500, 200 + additionalEdge),
                new Vector2(mapSize * 0.5, 200 + additionalEdge),
                new Vector2(mapSize - 1500, 200 + additionalEdge),
                new Vector2(mapSize - 1000, 200 + additionalEdge),
                new Vector2(mapSize - 500, 150 + additionalEdge),
                new Vector2(mapSize - 100, 150 + additionalEdge)
            });

            waypointsByLane.Add(LaneType.Bottom, new List<Vector2>() {
                new Vector2(100.0, mapSize - 150 + additionalEdge),
                new Vector2(500.0, mapSize - 150 + additionalEdge),
                new Vector2(1000.0, mapSize - 200 + additionalEdge),
                new Vector2(1500.0, mapSize - 200 + additionalEdge),
                new Vector2(mapSize * 0.5, mapSize - 200 + additionalEdge),
                new Vector2(mapSize - 1500.0, mapSize - 200 + additionalEdge),
                new Vector2(mapSize - 1000.0, mapSize - 200 + additionalEdge),

                new Vector2(mapSize - 600, mapSize - 200 + additionalEdge),
                new Vector2(mapSize - 400 + additionalEdge, mapSize - 400 + additionalEdge), //s
                new Vector2(mapSize - 200 + additionalEdge, mapSize - 600),

                new Vector2(mapSize - 200 + additionalEdge, mapSize - 1000),
                new Vector2(mapSize - 200 + additionalEdge, mapSize - 1500),
                new Vector2(mapSize - 200 + additionalEdge, mapSize * 0.5),
                new Vector2(mapSize - 200 + additionalEdge, 1500),
                new Vector2(mapSize - 200 + additionalEdge, 1000),
                new Vector2(mapSize - 150 + additionalEdge, 500),
                new Vector2(mapSize - 150 + additionalEdge, 100)
            });
        }

        private void InitializeBuildingsList()
        {
            Building exampleLittleBuilding = world.Buildings.OrderBy(x => x.Life).FirstOrDefault();
            Building exampleBigBuilding = world.Buildings.OrderByDescending(x => x.Life).FirstOrDefault();

            BuildingInfo building1 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 2070.71067811865,
                yPos = 1600.0,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building2 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 3650.0,
                yPos = 2343.25135533731,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building3 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 1687.87400257716,
                yPos = 50.0,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building4 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 3097.38694133282,
                yPos = 1231.90238054852,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building5 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 2629.3396796484,
                yPos = 350.0,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building6 = new BuildingInfo
            {
                hpCount = exampleLittleBuilding.Life,
                xPos = 3950.0,
                yPos = 1306.74222219166,
                attackRange = exampleLittleBuilding.AttackRange,
                radius = exampleLittleBuilding.Radius
            };

            BuildingInfo building7 = new BuildingInfo
            {
                hpCount = exampleBigBuilding.Life,
                xPos = 3600.0,
                yPos = 400.0,
                attackRange = exampleBigBuilding.AttackRange,
                radius = exampleBigBuilding.Radius
            };

            enemyBuildings.Add(GetBuildingCode(building1), building1);
            enemyBuildings.Add(GetBuildingCode(building2), building2);
            enemyBuildings.Add(GetBuildingCode(building3), building3);
            enemyBuildings.Add(GetBuildingCode(building4), building4);
            enemyBuildings.Add(GetBuildingCode(building5), building5);
            enemyBuildings.Add(GetBuildingCode(building6), building6);
            enemyBuildings.Add(GetBuildingCode(building7), building7);
        }

        private void UpdateLane()
        {
            switch (wizard.Id)
            {
                case 1:
                case 6:
                    currentLaneType = LaneType.Top;
                    break;
                case 2:
                case 3:
                case 4:
                case 7:
                case 8:
                case 9:
                    currentLaneType = LaneType.Middle;
                    break;
                case 5:
                case 10:
                    currentLaneType = LaneType.Bottom;
                    break;
            }
        }

        private Vector2 GetNextWaypoint()
        {
            int nearerIndex = 0;
            int allowableDistance = 225;
            int nearerDistance = int.MaxValue;

            for (int i = 0; i < waypointsByLane[currentLaneType].Count; i++)
            {
                if (wizard.GetDistanceTo(
                    waypointsByLane[currentLaneType][i].x,
                    waypointsByLane[currentLaneType][i].y)
                    < nearerDistance)
                {
                    nearerIndex = i;
                    nearerDistance = (int)wizard.GetDistanceTo(
                        waypointsByLane[currentLaneType][i].x,
                        waypointsByLane[currentLaneType][i].y);
                }
            }

            int selectedIndex;
            if (nearerIndex + 1 < waypointsByLane[currentLaneType].Count
                &&
                GetDistance(
                    waypointsByLane[currentLaneType][nearerIndex].x,
                    waypointsByLane[currentLaneType][nearerIndex].y,
                    waypointsByLane[currentLaneType][nearerIndex + 1].x,
                    waypointsByLane[currentLaneType][nearerIndex + 1].y)
                    + allowableDistance >
                wizard.GetDistanceTo(
                    waypointsByLane[currentLaneType][nearerIndex + 1].x,
                    waypointsByLane[currentLaneType][nearerIndex + 1].y))
                selectedIndex = nearerIndex + 1;
            else
                selectedIndex = nearerIndex;

            if ((2400 < wizard.X && wizard.X < 3400 &&
                2400 < wizard.Y && wizard.Y < 3400) ||
                (600 < wizard.X && wizard.X < 1600 &&
                600 < wizard.Y && wizard.Y < 1600))
                selectedIndex = waypointsByLane[currentLaneType].Count / 2;

            return waypointsByLane[currentLaneType][selectedIndex];
        }

        private Vector2 GetPreviousWaypoint()
        {
            int nearerIndex = 0;
            int allowableDistance = 150;
            int nearerDistance = int.MaxValue;

            for (int i = 0; i < waypointsByLane[currentLaneType].Count; i++)
            {
                if (wizard.GetDistanceTo(
                    waypointsByLane[currentLaneType][i].x,
                    waypointsByLane[currentLaneType][i].y)
                    < nearerDistance)
                {
                    nearerIndex = i;
                    nearerDistance = (int)wizard.GetDistanceTo(
                        waypointsByLane[currentLaneType][i].x,
                        waypointsByLane[currentLaneType][i].y);
                }
            }

            if (nearerIndex != 0
                &&
                GetDistance(
                    waypointsByLane[currentLaneType][nearerIndex].x,
                    waypointsByLane[currentLaneType][nearerIndex].y,
                    waypointsByLane[currentLaneType][nearerIndex - 1].x,
                    waypointsByLane[currentLaneType][nearerIndex - 1].y)
                    + allowableDistance >
                wizard.GetDistanceTo(
                    waypointsByLane[currentLaneType][nearerIndex - 1].x,
                    waypointsByLane[currentLaneType][nearerIndex - 1].y))

                return waypointsByLane[currentLaneType][nearerIndex - 1];
            else return waypointsByLane[currentLaneType][nearerIndex];
        }

        private LivingUnit GetNearestTarget()
        {
            List<LivingUnit> targets = GetEnemies();

            LivingUnit nearestTarget = null;
            double nearestTargetDistance = Double.MaxValue;

            foreach (var target in targets)
            {
                double distance = wizard.GetDistanceTo(target);

                if (distance < nearestTargetDistance)
                {
                    nearestTarget = target;
                    nearestTargetDistance = distance;
                }
            }

            return nearestTarget;
        }

        private int GetNearestTargetThatICanShootCount()
        {
            List<LivingUnit> targets = GetEnemies();

            int count = 0;
            foreach (var target in targets)
            {
                double distance = wizard.GetDistanceTo(target);

                if (distance < game.WizardCastRange)
                {
                    count++;
                }
            }

            return count;
        }

        private int GetNearestTargetThatCanShootMeCount()
        {
            List<LivingUnit> targets = GetEnemiesThanCanShoot();

            int count = 0;
            foreach (var target in targets)
            {
                double distance = wizard.GetDistanceTo(target);

                if (distance <= game.WizardCastRange + _maxAdditionalCastRange + 50
                    ||
                    distance <= game.GuardianTowerAttackRange)
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsHindranceAtDirection(Vector2 walkDirection)
        {
            int _personalSpace = 35;
            Vector2 directional = Rotate(walkDirection, wizard.Angle);

            List<CircularUnit> units = GetAllNotMe().Where(x =>
               wizard.GetDistanceTo(x) < wizard.Radius + x.Radius + _personalSpace
               && Math.Abs(GetAngle(
                   new Vector2(directional.x, directional.y),
                   new Vector2(x.X - wizard.X, x.Y - wizard.Y))) < Math.PI / 2).ToList();

            Vector2 destination = new Vector2(wizard.X + directional.x, wizard.Y + directional.y);
            Vector2 route = new Vector2(destination.x - wizard.X, destination.y - wizard.Y);

            Vector2 ort = directional / directional.Length();
            double yToLeft = ort.x / ort.y;
            Vector2 toLeft = new Vector2(route.y < 0 ? -1 : 1, route.y < 0 ? yToLeft : -yToLeft);
            Vector2 ortToLeft = toLeft / toLeft.Length();
            Vector2 radiusToLeft = ortToLeft * wizard.Radius * 1.05;
            Vector2 pointFromLeft = new Vector2(wizard.X + radiusToLeft.x, wizard.Y + radiusToLeft.y);
            Vector2 pointToLeft = new Vector2(destination.x + radiusToLeft.x, destination.y + radiusToLeft.y);

            double yToRight = -ort.x / ort.y;
            Vector2 toRight = new Vector2(route.y < 0 ? 1 : -1, route.y < 0 ? yToRight : -yToRight);
            Vector2 ortToRight = toRight / toRight.Length();
            Vector2 radiusToRight = ortToRight * wizard.Radius * 1.05;
            Vector2 pointFromRight = new Vector2(wizard.X + radiusToRight.x, wizard.Y + radiusToRight.y);
            Vector2 pointToRight = new Vector2(destination.x + radiusToRight.x, destination.y + radiusToRight.y);

#if LOCAL
            vc.Line(pointFromLeft.x, pointFromLeft.y, pointToLeft.x, pointToLeft.y, 1f, 0f, 0f);
            vc.Line(pointFromRight.x, pointFromRight.y, pointToRight.x, pointToRight.y, 1f, 0f, 0f);
#endif

            bool isAnyInRectangle = false;
            foreach (var unit in units)
            {
                if (UnitInRectancle(unit as LivingUnit, pointFromLeft, pointToLeft, pointToRight, pointFromRight))
                {
                    isAnyInRectangle = true;
#if LOCAL
                vc.FillCircle(unit.X, unit.Y, (float)unit.Radius, 0f, 0f, 1f);
#endif
                }
            }

            return isAnyInRectangle;
        }

        private Vector2 TreeOnPath(Vector2 walkDirection)
        {
            double _distanceToTree = wizard.VisionRange;

            List<Tree> units = world.Trees.Where(x => wizard.GetDistanceTo(x) < _distanceToTree
                && Math.Abs(GetAngle(walkDirection, new Vector2(x.X - wizard.X, x.Y - wizard.Y))) < Math.PI / 4).ToList();

            Vector2 directional = Rotate(walkDirection, wizard.Angle);
            Vector2 destination = new Vector2(wizard.X + directional.x, wizard.Y + directional.y);
            Vector2 route = new Vector2(destination.x - wizard.X, destination.y - wizard.Y);

            Vector2 ort = directional / directional.Length();
            double yToLeft = ort.x / ort.y;
            Vector2 toLeft = new Vector2(route.y < 0 ? -1 : 1, route.y < 0 ? yToLeft : -yToLeft);
            Vector2 ortToLeft = toLeft / toLeft.Length();
            Vector2 radiusToLeft = ortToLeft * wizard.Radius * 1.05;
            Vector2 pointFromLeft = new Vector2(wizard.X + radiusToLeft.x, wizard.Y + radiusToLeft.y);
            Vector2 pointToLeft = new Vector2(destination.x + radiusToLeft.x, destination.y + radiusToLeft.y);

            double yToRight = -ort.x / ort.y;
            Vector2 toRight = new Vector2(route.y < 0 ? 1 : -1, route.y < 0 ? yToRight : -yToRight);
            Vector2 ortToRight = toRight / toRight.Length();
            Vector2 radiusToRight = ortToRight * wizard.Radius * 1.05;
            Vector2 pointFromRight = new Vector2(wizard.X + radiusToRight.x, wizard.Y + radiusToRight.y);
            Vector2 pointToRight = new Vector2(destination.x + radiusToRight.x, destination.y + radiusToRight.y);

#if LOCAL
            vc.Line(pointFromLeft.x, pointFromLeft.y, pointToLeft.x, pointToLeft.y, 1f, 0f, 0f);
            vc.Line(pointFromRight.x, pointFromRight.y, pointToRight.x, pointToRight.y, 1f, 0f, 0f);
#endif

            List<Tree> treesOnPath = new List<Tree>();
            foreach (var unit in units)
            {
                if (UnitInRectancle(unit as LivingUnit, pointFromLeft, pointToLeft, pointToRight, pointFromRight))
                {
                    treesOnPath.Add(unit);
#if LOCAL
                    vc.FillCircle(unit.X, unit.Y, (float)unit.Radius, 0.5f, 0f, 1f);
#endif
                }
            }

            return treesOnPath.OrderBy(x => wizard.GetDistanceTo(x)).Select(x => new Vector2(x.X, x.Y)).FirstOrDefault();
        }

        private double GetEmptySpaceDirection(Vector2 destination)
        {
            double myRadius = wizard.Radius * 1.1;
            Vector2 fromWizToDestination = new Vector2(destination.x - wizard.X, destination.y - wizard.Y);

            int personalSpace = 35;

            var units = GetAllNotMe()
                .Where(o => GetDistance(wizard.X, wizard.Y, o.X, o.Y)
                    <= wizard.Radius + o.Radius + personalSpace).ToList();

            Vector2 rotatedDirection, newDestination, route, ort;
            Vector2 toLeft, ortToLeft, radiusToLeft, pointFromLeft, pointToLeft;
            Vector2 toRight, ortToRight, radiusToRight, pointFromRight, pointToRight;
            double yToLeft, yToRight;

            for (int alpha = 5; alpha <= 175; alpha += 5)
            {
                rotatedDirection = Rotate(fromWizToDestination, alpha * fromDegreesToRadians);
                newDestination = new Vector2(wizard.X + rotatedDirection.x, wizard.Y + rotatedDirection.y);
                route = new Vector2(newDestination.x - wizard.X, newDestination.y - wizard.Y);

                ort = rotatedDirection / rotatedDirection.Length();
                yToLeft = ort.x / ort.y;
                toLeft = new Vector2(route.y < 0 ? -1 : 1, route.y < 0 ? yToLeft : -yToLeft);
                ortToLeft = toLeft / toLeft.Length();
                radiusToLeft = ortToLeft * myRadius;
                pointFromLeft = new Vector2(wizard.X + radiusToLeft.x, wizard.Y + radiusToLeft.y);
                pointToLeft = new Vector2(newDestination.x + radiusToLeft.x, newDestination.y + radiusToLeft.y);

                yToRight = -ort.x / ort.y;
                toRight = new Vector2(route.y < 0 ? 1 : -1, route.y < 0 ? yToRight : -yToRight);
                ortToRight = toRight / toRight.Length();
                radiusToRight = ortToRight * myRadius;
                pointFromRight = new Vector2(wizard.X + radiusToRight.x, wizard.Y + radiusToRight.y);
                pointToRight = new Vector2(newDestination.x + radiusToRight.x, newDestination.y + radiusToRight.y);

                bool isAnyInRectangle = false;
                foreach (var unit in units)
                {
                    if (UnitInRectancle(unit as LivingUnit, pointFromLeft, pointToLeft, pointToRight, pointFromRight))
                    {
                        isAnyInRectangle = true;
                    }
                }
                if (!isAnyInRectangle) return alpha;

                rotatedDirection = Rotate(fromWizToDestination, -alpha * fromDegreesToRadians);
                newDestination = new Vector2(wizard.X + rotatedDirection.x, wizard.Y + rotatedDirection.y);
                route = new Vector2(newDestination.x - wizard.X, newDestination.y - wizard.Y);

                ort = rotatedDirection / rotatedDirection.Length();
                yToLeft = ort.x / ort.y;
                toLeft = new Vector2(route.y < 0 ? -1 : 1, route.y < 0 ? yToLeft : -yToLeft);
                ortToLeft = toLeft / toLeft.Length();
                radiusToLeft = ortToLeft * myRadius;
                pointFromLeft = new Vector2(wizard.X + radiusToLeft.x, wizard.Y + radiusToLeft.y);
                pointToLeft = new Vector2(newDestination.x + radiusToLeft.x, newDestination.y + radiusToLeft.y);

                yToRight = -ort.x / ort.y;
                toRight = new Vector2(route.y < 0 ? 1 : -1, route.y < 0 ? yToRight : -yToRight);
                ortToRight = toRight / toRight.Length();
                radiusToRight = ortToRight * myRadius;
                pointFromRight = new Vector2(wizard.X + radiusToRight.x, wizard.Y + radiusToRight.y);
                pointToRight = new Vector2(newDestination.x + radiusToRight.x, newDestination.y + radiusToRight.y);

                isAnyInRectangle = false;
                foreach (var unit in units)
                {
                    if (UnitInRectancle(unit as LivingUnit, pointFromLeft, pointToLeft, pointToRight, pointFromRight))
                    {
                        isAnyInRectangle = true;
                    }
                }
                if (!isAnyInRectangle) return -alpha;

            }

            return 0;
        }

        private bool WoodcuttersBeside()
        {
            int personalSpace = 90;

            var woodcutters = world.Minions
                .Where(x => x.Faction != wizard.Faction && x.Faction != Faction.Neutral)
                .Where(x => wizard.GetDistanceTo(x) < wizard.Radius + x.Radius + personalSpace)
                .ToList();

            woodcutters.AddRange(world.Minions
                .Where(x => x.Faction == Faction.Neutral)
                .Where(x => wizard.GetDistanceTo(x) < wizard.Radius + x.Radius + personalSpace)
                .Where(x => x.Life < x.MaxLife || x.RemainingActionCooldownTicks != 0)
                );

            if (woodcutters.Count() > 0) return true;
            else return false;
        }

        private bool IAmAtTheForefrontAndUnderFire()
        {
            float precaution = 50.0f;

            if (wizard.X > wizard.Y - game.MapSize / 10 && wizard.X < wizard.Y + game.MapSize / 10
                &&
                ((600 < wizard.X && wizard.X < 1700 && 700 < wizard.Y && wizard.Y < 1700)
                    ||
                (2300 < wizard.X && wizard.X < 3300 && 2300 < wizard.Y && wizard.Y < 3400)))
                return false;

            var enemies = new List<LivingUnit>();
            enemies.AddRange(world.Wizards
                .Where(x => x.Faction != wizard.Faction)
                .Where(x => wizard.GetDistanceTo(x) <= x.CastRange + x.Radius + wizard.Radius + precaution)
                .Where(x => Math.Abs(wizard.GetAngleTo(x)) < Math.PI / 2));
            enemies.AddRange(world.Minions
                .Where(x => x.Faction != wizard.Faction && x.Faction != Faction.Neutral)
                .Where(x => x.Type == MinionType.FetishBlowdart)
                .Where(x => wizard.GetDistanceTo(x) <= game.FetishBlowdartAttackRange + wizard.Radius + precaution)
                .Where(x => Math.Abs(wizard.GetAngleTo(x)) < Math.PI / 2));

            int additionalEnemyByAssumptionCount = 0;

            foreach (var building in enemyBuildings)
            {
                if (wizard.GetDistanceTo(building.Value.xPos, building.Value.yPos)
                    <= building.Value.attackRange + building.Value.radius + wizard.Radius + precaution)
                    additionalEnemyByAssumptionCount++;
            }

            var friends = new List<LivingUnit>();
            friends.AddRange(world.Buildings
                .Where(x => x.Faction == wizard.Faction)
                .Where(x => wizard.GetDistanceTo(x) <= game.WizardVisionRange / 2)
                .Where(x => Math.Abs(GetAngle(new Vector2(GetNextWaypoint().x - wizard.X, GetNextWaypoint().y - wizard.Y),
                    new Vector2(x.X - wizard.X, x.Y - wizard.Y))) < Math.PI * 0.4f));
            friends.AddRange(world.Wizards
                .Where(x => x.Faction == wizard.Faction)
                .Where(x => !x.IsMe)
                //.Where(x => wizard.GetDistanceTo(x) <= game.WizardVisionRange)
                .Where(x => GetDistance(x.X, x.Y, GetNextWaypoint().x, GetNextWaypoint().y) < wizard.VisionRange)
                .Where(x => Math.Abs(GetAngle(new Vector2(GetNextWaypoint().x - wizard.X, GetNextWaypoint().y - wizard.Y),
                    new Vector2(x.X - wizard.X, x.Y - wizard.Y))) < Math.PI * 0.5f));
            friends.AddRange(world.Minions
                .Where(x => x.Faction == wizard.Faction)
                //.Where(x => wizard.GetDistanceTo(x) <= game.WizardVisionRange * 2.5)
                .Where(x => GetDistance(x.X, x.Y, GetNextWaypoint().x, GetNextWaypoint().y) < wizard.VisionRange)
                .Where(x => Math.Abs(GetAngle(new Vector2(GetNextWaypoint().x - wizard.X, GetNextWaypoint().y - wizard.Y),
                    new Vector2(x.X - wizard.X, x.Y - wizard.Y))) < Math.PI * 0.45f));

            if (enemies.Count + additionalEnemyByAssumptionCount > 0 && friends.Count == 0)
                return true;
            else return false;
        }

        private void RotateTo(Vector2 waypoint)
        {
            double turnAngle = wizard.GetAngleTo(waypoint.x, waypoint.y);
            move.Turn = turnAngle;
        }

        private void Walk(Vector2 waypoint, bool checkTree = false)
        {
            Vector2 fromWizardToWaypoint = new Vector2(waypoint.x - wizard.X, waypoint.y - wizard.Y);

            Vector2 walkVector = Rotate(fromWizardToWaypoint, -wizard.Angle);

            bool treeShoppingMode = false;
            if (checkTree)
            {
                var treeOnPath = TreeOnPath(walkVector);
                if (treeOnPath != null)
                {
                    ChopTree(treeOnPath);
                    treeShoppingMode = true;
                }
            }
            if (!treeShoppingMode && IsHindranceAtDirection(walkVector))
            {
                double correctionAngle = GetEmptySpaceDirection(waypoint);
                walkVector = Rotate(walkVector, correctionAngle * fromDegreesToRadians);
            }

#if LOCAL
            vc.Line(wizard.X, wizard.Y, wizard.X + fromWizardToWaypoint.x, wizard.Y + fromWizardToWaypoint.y, 0.5f, 1.0f, 0.0f);
            vc.Line(wizard.X, wizard.Y, wizard.X + Rotate(walkVector, wizard.Angle).x, wizard.Y + Rotate(walkVector, wizard.Angle).y, 1.0f, 0.7f, 0.5f);
#endif

            double factor = (wizard.Statuses.Any(x => x.Type == StatusType.Hastened)) ? 1.3 : 1.0;
            double xSpeed = walkVector.x < 0 ? 3.0 * factor : 4.0 * factor, ySpeed = 3.0 * factor;
            double devider = Math.Sqrt(Math.Pow(Math.Abs(walkVector.x / xSpeed), 2) + Math.Pow(Math.Abs(walkVector.y / ySpeed), 2));

            walkVector /= devider;

            move.Speed = walkVector.x;
            move.StrafeSpeed = walkVector.y;
        }

        private void GoToNearestBonus(bool rotateToBonus)
        {
            var allBonuses = new List<BonusInfo>();
            allBonuses.AddRange(bonuses);
            allBonuses.AddRange(bonusesInAdvance);

            BonusInfo bonus = allBonuses.OrderBy(x => wizard.GetDistanceTo(x.X, x.Y)).First();
            Walk(new Vector2(bonus.X, bonus.Y), true);
            if (rotateToBonus) move.Turn = wizard.GetAngleTo(bonus.X, bonus.Y);
        }

        private bool ShouldGoToBonus()
        {
            double allowableDistanceToBonus = game.MapSize / 2;
            if (!bonuses.Any(x => wizard.GetDistanceTo(x.X, x.Y) < allowableDistanceToBonus)
                &&
                !bonusesInAdvance.Any(x => wizard.GetDistanceTo(x.X, x.Y) < allowableDistanceToBonus)) return false;

            if (currentLaneType == LaneType.Bottom)
            {
                if (wizard.X > game.MapSize / 2 && wizard.Y > game.MapSize / 2
                    && wizard.X > wizard.Y - game.MapSize / 10 && wizard.X < wizard.Y + game.MapSize / 10)
                    return true;
                else
                    return false;
            }

            if (currentLaneType == LaneType.Top)
            {
                if (wizard.X < game.MapSize / 2 && wizard.Y < game.MapSize / 2
                    && wizard.X > wizard.Y - game.MapSize / 10 && wizard.X < wizard.Y + game.MapSize / 10)
                    return true;
                else
                    return false;
            }

            if (currentLaneType == LaneType.Middle)
            {
                if (wizard.X > game.MapSize * 0.2 && wizard.Y > game.MapSize * 0.2
                    &&
                    wizard.X < game.MapSize * 0.8 && wizard.Y < game.MapSize * 0.8
                    &&
                    wizard.X > wizard.Y - game.MapSize / 10 && wizard.X < wizard.Y + game.MapSize / 10)
                    return true;
                else
                    return false;
            }
            return false;
        }

        private void ChopTree(Vector2 treePosition)
        {
            var angle = wizard.GetAngleTo(treePosition.x, treePosition.y);
            if (Math.Abs(angle) < Math.PI / 12)
            {
                move.CastAngle = angle;
                move.Action = ActionType.MagicMissile;
            }
            move.Turn = angle;
        }

        private void Fire()
        {
            List<LivingUnit> targets = GetEnemies();

            List<LivingUnit> aimsNear = targets
                .Where(x => wizard.GetDistanceTo(x) < game.WizardCastRange)
                .OrderByDescending(x => wizard.GetDistanceTo(x))
                .ToList();

            LivingUnit aim = aimsNear.OrderBy(x => x.Life).FirstOrDefault();

            if (aim == null)
            {
                return;
            }

            if (Math.Abs(wizard.GetAngleTo(aim)) < Math.PI / 12)
            {
                ActionType actionType = GetBestActionType(aim);
                Vector2 aimPosition = GetFiringPoint(aim, actionType);
                move.CastAngle = wizard.GetAngleTo(aimPosition.x, aimPosition.y);
                move.MinCastDistance = wizard.GetDistanceTo(aim) - aim.Radius;
                move.Action = actionType;
            }
            else
            {
                move.Turn = wizard.GetAngleTo(aim);
            }
        }

        private ActionType GetBestActionType(LivingUnit aim)
        {
            if (wizard.Skills.Contains(SkillType.FrostBolt)
                &&
                (aim is Wizard || aim is Minion)
                &&
                wizard.Mana >= game.FrostBoltManacost + 10 //magic number 
                &&
                aim.Life >= aim.MaxLife * 0.3f // another one
                &&
                wizard.RemainingCooldownTicksByAction[(int)ActionType.FrostBolt] == 0)
                return ActionType.FrostBolt;

            return ActionType.MagicMissile;
        }

        private void FireIfCan()
        {
            List<LivingUnit> targets = GetEnemies();

            var aim = targets
                .Where(x => wizard.GetDistanceTo(x) < wizard.CastRange)
                .OrderBy(x => wizard.GetDistanceTo(x))
                .FirstOrDefault();

            if (aim != null)
            {
                move.Turn = wizard.GetAngleTo(aim);
                if (wizard.RemainingActionCooldownTicks != 0) return;

                if (wizard.RemainingCooldownTicksByAction[(int)ActionType.MagicMissile] >= 30
                    &&
                        (!wizard.Skills.Contains(SkillType.FrostBolt)
                        ||
                        wizard.RemainingCooldownTicksByAction[(int)ActionType.FrostBolt] >= 30)
                    &&
                    wizard.RemainingCooldownTicksByAction[(int)ActionType.Staff] == 0)
                {
                    var staffAims = targets
                        .Where(x => wizard.GetDistanceTo(x) <= x.Radius + 70.0f
                        && wizard.GetAngleTo(x) <= Math.PI / 12).ToList();

                    if (staffAims.Count > 0)
                        move.Action = ActionType.Staff;

                    return;
                }

                ActionType actionType = GetBestActionType(aim);

                if (Math.Abs(wizard.GetAngleTo(aim)) < Math.PI / 12)
                {
                    Vector2 firingPoint = GetFiringPoint(aim, actionType);
                    move.CastAngle = wizard.GetAngleTo(firingPoint.x, firingPoint.y);
                    move.Action = actionType;
                }
            }
        }

        private Vector2 GetFiringPoint(LivingUnit aim, ActionType actionType)
        {
            double distanceToAim = wizard.GetDistanceTo(aim);
            double actionTypeSpeed = 40.0;
            switch (actionType)
            {
                case ActionType.MagicMissile:
                    actionTypeSpeed = 40.0;
                    break;
                case ActionType.FrostBolt:
                    actionTypeSpeed = 35.0;
                    break;
                case ActionType.Fireball:
                    actionTypeSpeed = 30.0;
                    break;
                default:
                    actionTypeSpeed = 40.0;
#if LOCAL
                    Console.WriteLine("Action type +'" + actionType.ToString() + "' is absent in switch-case statement");
#endif
                    break;
            }
            double ticksToApproximate = (distanceToAim / actionTypeSpeed) / 2;

            if (enemyUnits.ContainsKey(aim.Id))
            {
                Vector2 lastMovement = new Vector2(aim.X - enemyUnits[aim.Id].xPos, aim.Y - enemyUnits[aim.Id].yPos);
                return new Vector2(aim.X + ticksToApproximate * lastMovement.x, aim.Y + ticksToApproximate * lastMovement.y);
            }
            else return new Vector2(aim.X, aim.Y);
        }

        private List<LivingUnit> GetEnemies()
        {
            List<LivingUnit> targets = new List<LivingUnit>();
            targets.AddRange(world.Buildings.Where(x => x.Faction != wizard.Faction));
            targets.AddRange(world.Wizards.Where(x => x.Faction != wizard.Faction));
            targets.AddRange(world.Minions.Where(x => x.Faction != wizard.Faction && x.Faction != Faction.Neutral));
            return targets;
        }

        private List<LivingUnit> GetEnemiesThanCanShoot()
        {
            List<LivingUnit> targets = new List<LivingUnit>();
            targets.AddRange(world.Buildings.Where(x => x.Faction != wizard.Faction));
            targets.AddRange(world.Wizards.Where(x => x.Faction != wizard.Faction));
            targets.AddRange(world.Minions
                .Where(x => x.Faction != wizard.Faction
                && x.Faction != Faction.Neutral
                && x.Type != MinionType.OrcWoodcutter));

            return targets;
        }

        private List<LivingUnit> GetNotEnemies()
        {
            List<LivingUnit> targets = new List<LivingUnit>();
            targets.AddRange(world.Buildings.Where(x => x.Faction == wizard.Faction));
            targets.AddRange(world.Wizards.Where(x => x.Faction == wizard.Faction && !x.IsMe));
            targets.AddRange(world.Minions.Where(x => x.Faction == wizard.Faction));
            targets.AddRange(world.Trees);
            return targets;
        }

        private List<CircularUnit> GetAllNotMe()
        {
            List<CircularUnit> targets = new List<CircularUnit>();
            targets.AddRange(world.Buildings);
            targets.AddRange(world.Wizards.Where(x => !x.IsMe));
            targets.AddRange(world.Minions);
            targets.AddRange(world.Trees);
            return targets;
        }

        private void CheckStopping()
        {
            if (!crazyMode
                && wizard.X == prevX && wizard.Y == prevY
                && (move.Speed != 0.0 || move.StrafeSpeed != 0.0))
            {
                waitTick++;
                if (waitTick >= 10)
                {
                    crazyMode = true;
                    waitTick *= 2;
                }
            }

            if (crazyMode)
            {
                if (waitTick > 0)
                {
                    move.Speed = rand.Next() % 2 == 0 ? game.WizardForwardSpeed : -game.WizardBackwardSpeed;
                    move.StrafeSpeed = rand.Next() % 2 == 0 ? game.WizardStrafeSpeed : -game.WizardStrafeSpeed;
                    waitTick--;
                }
                else
                {
                    crazyMode = false;
                    waitTick = 0;
                }
            }

            if (wizard.X != prevX || wizard.Y != prevY)
            {
                waitTick = 0;
                prevX = wizard.X;
                prevY = wizard.Y;
            }
        }

        private void TickEnd()
        {
            ActualizeBuildingDictionary();
            ActualizeEnemiesDictionary();
            ActualizeBonusesList();
            ActualizeBonusesInAdvanceList();
            ActualizeDeathCounter();
            CheckSkills();

            lastTick = world.TickIndex;
        }

        private void ActualizeDeathCounter()
        {
            if (world.TickIndex > lastTick + 1195)
                deathNumber++;
        }

        private void ActualizeEnemiesDictionary()
        {
            enemyUnits.Clear();

            List<LivingUnit> units = new List<LivingUnit>();
            units.AddRange(world.Minions.Where(x => x.Faction != wizard.Faction));
            units.AddRange(world.Wizards.Where(x => x.Faction != wizard.Faction));

            foreach (var unit in units)
            {
                enemyUnits.Add(unit.Id, new UnitUnfo()
                {
                    xPos = unit.X,
                    yPos = unit.Y
                });
            }
        }

        private void ActualizeBuildingDictionary()
        {
            foreach (var building in world.Buildings.Where(x => x.Faction != wizard.Faction))
            {
                if (enemyBuildings.ContainsKey(GetBuildingCode(building.X, building.Y)))
                {
                    enemyBuildings[GetBuildingCode(building.X, building.Y)].hpCount = building.Life;
                    enemyBuildings[GetBuildingCode(building.X, building.Y)].attackRange = building.AttackRange;
                    enemyBuildings[GetBuildingCode(building.X, building.Y)].radius = building.Radius;
                    enemyBuildings[GetBuildingCode(building.X, building.Y)].xPos = building.X;
                    enemyBuildings[GetBuildingCode(building.X, building.Y)].yPos = building.Y;
                }
            }

            List<LivingUnit> friends = new List<LivingUnit>();
            friends.AddRange(world.Wizards.Where(x => x.Faction == wizard.Faction));
            friends.AddRange(world.Minions.Where(x => x.Faction == wizard.Faction));

            float minionVisionRange = 400.0f;

            bool oneRemoving = false;
            foreach (var building in enemyBuildings)
            {
                foreach (var friend in friends)
                {
                    if (friend is Wizard
                        && GetDistance(friend.X, friend.Y, building.Value.xPos, building.Value.yPos) < game.WizardVisionRange - building.Value.radius / 2
                        && world.Buildings.Where(x => GetBuildingCode(x.X, x.Y) == building.Key).Count() == 0)
                    {
                        enemyBuildings.Remove(building.Key);
                        oneRemoving = true;
                    }
                    else if (friend is Minion
                        && GetDistance(friend.X, friend.Y, building.Value.xPos, building.Value.yPos) < minionVisionRange - building.Value.radius / 2
                        && world.Buildings.Where(x => GetBuildingCode(x.X, x.Y) == building.Key).Count() == 0)
                    {
                        enemyBuildings.Remove(building.Key);
                        oneRemoving = true;
                    }
                    if (oneRemoving) break;
                }
                if (oneRemoving) break;
            }
        }

        private void ActualizeBonusesList()
        {
            if (world.TickIndex > 0 && world.TickIndex % 2500 == 0)
            {
                bonuses.Clear();
                bonuses.Add(new BonusInfo() { X = 1200, Y = 1200 });
                bonuses.Add(new BonusInfo() { X = 2800, Y = 2800 });
                return;
            }

            if (world.Bonuses.Count() == 2 && bonuses.Count() == 2) return;
            if (world.Bonuses.Count() == 1 && bonuses.Count() == 1
                && world.Bonuses[0].X < game.MapSize / 2 && bonuses[0].X < game.MapSize / 2) return;
            if (world.Bonuses.Count() == 1 && bonuses.Count() == 1
                && world.Bonuses[0].X > game.MapSize / 2 && bonuses[0].X > game.MapSize / 2) return;
            if (world.Bonuses.Count() == 0 && bonuses.Count() == 0) return;

            List<LivingUnit> units = new List<LivingUnit>();
            units.AddRange(world.Wizards.Where(x => x.Faction == wizard.Faction).ToList());
            units.AddRange(world.Minions.Where(x => x.Faction == wizard.Faction).ToList());

            double reserve = 10.0;
            foreach (var unit in units)
            {
                if (unit is Wizard
                    &&
                    unit.GetDistanceTo(1200, 1200) <= game.WizardVisionRange - reserve
                    &&
                    !world.Bonuses.Any(x => x.X < game.MapSize / 2))
                    bonuses.Remove(bonuses.Where(x => x.X < game.MapSize / 2).FirstOrDefault());

                if (unit is Minion
                    &&
                    unit.GetDistanceTo(1200, 1200) <= game.MinionVisionRange - reserve
                    &&
                    !world.Bonuses.Any(x => x.X < game.MapSize / 2))
                    bonuses.Remove(bonuses.Where(x => x.X < game.MapSize / 2).FirstOrDefault());

                if (unit is Wizard
                    &&
                    unit.GetDistanceTo(2800, 2800) <= game.WizardVisionRange - reserve
                    &&
                    !world.Bonuses.Any(x => x.X > game.MapSize / 2))
                    bonuses.Remove(bonuses.Where(x => x.X > game.MapSize / 2).FirstOrDefault());

                if (unit is Minion
                    &&
                    unit.GetDistanceTo(2800, 2800) <= game.MinionVisionRange - reserve
                    &&
                    !world.Bonuses.Any(x => x.X > game.MapSize / 2))
                    bonuses.Remove(bonuses.Where(x => x.X < game.MapSize / 2).FirstOrDefault());
            }
        }

        private void ActualizeBonusesInAdvanceList()
        {
            bonusesInAdvance.Clear();

            if (world.TickIndex == 0) return;
            int toBonusAppear = (2500 - world.TickIndex % 2500);
            if (toBonusAppear > 500) return;

            double reserve = 10.0;
            double bonusRadius = 20.0;
            double letSpeed = (wizard.Statuses.Any(x => x.Type == StatusType.Hastened)) ? 5.2 : 4;

            if ((wizard.GetDistanceTo(1200, 1200) - wizard.Radius - bonusRadius - reserve) / letSpeed > toBonusAppear)
                bonusesInAdvance.Add(new BonusInfo() { X = 1200, Y = 1200 });

            if ((wizard.GetDistanceTo(2800, 2800) - wizard.Radius - bonusRadius - reserve) / letSpeed > toBonusAppear)
                bonusesInAdvance.Add(new BonusInfo() { X = 2800, Y = 2800 });

            if (bonusesInAdvance.Count < 2) bonusesInAdvance.Clear();
        }

        private int GetBuildingCode(BuildingInfo info)
        {
            return GetBuildingCode(info.xPos, info.yPos);
        }

        private int GetBuildingCode(double xPos, double yPos)
        {
            return (int)(Math.Floor(xPos) * 5000 + Math.Floor(yPos));
        }

        //координаты прямоугольника задаются по часовой стрелке.
        private bool UnitInRectancle(LivingUnit unit, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (PointInRectangle(new Vector2(unit), a, b, c, d))
                return true;

            if (IsPointLeftOfLine(new Vector2(unit), c, d))
            {
                if (GetDistanceFromPointToLine(new Vector2(unit), c, d) < unit.Radius
                    && wizard.GetDistanceTo(unit) < GetDistance(wizard.X, wizard.Y, c.x, c.y))
                    return true;
            }

            if (IsPointLeftOfLine(new Vector2(unit), a, b))
            {
                if (GetDistanceFromPointToLine(new Vector2(unit), a, b) < unit.Radius
                    && wizard.GetDistanceTo(unit) < GetDistance(wizard.X, wizard.Y, b.x, b.y))
                    return true;
            }

            return false;
        }

        private double GetDistanceFromPointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            return
                Math.Abs(
                    (
                        (point.x - lineStart.x) * (lineEnd.y - lineStart.y) -
                        (point.y - lineStart.y) * (lineEnd.x - lineStart.x)
                    )
                    /
                    (
                        Math.Sqrt(Math.Pow(lineEnd.x - lineStart.x, 2)
                        + Math.Pow(lineEnd.y - lineStart.y, 2))
                    )
                );
        }

        //координаты прямоугольника задаются по часовой стрелке.
        private bool PointInRectangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int count = 0;
            if (IsPointRightOfLine(point, a, b)) count++;
            if (IsPointRightOfLine(point, b, c)) count++;
            if (IsPointRightOfLine(point, c, d)) count++;
            if (IsPointRightOfLine(point, d, a)) count++;

            if (count == 4) return true;
            else return false;
        }

        private bool IsPointRightOfLine(Vector2 point, Vector2 a, Vector2 b)
        {
            double d = (point.x - a.x) * (b.y - a.y) - (point.y - a.y) * (b.x - a.x);
            if (d <= 0) return true;
            else return false;
        }

        private bool IsPointLeftOfLine(Vector2 point, Vector2 a, Vector2 b)
        {
            double d = (point.x - a.x) * (b.y - a.y) - (point.y - a.y) * (b.x - a.x);
            if (d >= 0) return true;
            else return false;
        }

        private Vector2 Rotate(Vector2 vector, double angle)
        {
            return new Vector2(
                -Math.Sin(angle) * vector.y + Math.Cos(angle) * vector.x,
                Math.Cos(angle) * vector.y + Math.Sin(angle) * vector.x);
        }

        private double GetDistance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }

        private double GetAngle(Vector2 a, Vector2 b)
        {
            return Math.Atan2(a.x * b.y - b.x * a.y, a.x * b.x + a.y * b.y);
        }
    }
}