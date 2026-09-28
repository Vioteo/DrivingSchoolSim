namespace DrivingSchool.Input
{
    /// <summary>
    /// Органы управления в поездке, которые можно назначить на клавишу или кнопку руля (T42).
    /// Gas…Clutch у руля — оси (калибровка), у клавиатуры — клавиши.
    /// </summary>
    public enum DriveAction
    {
        Gas, Brake, SteerLeft, SteerRight, Clutch,
        Ignition, Starter, Handbrake, Belt, LeftSignal, RightSignal, Hazard, Lights, HighBeam, Flash, Horn, Wipers, Washer,
        Park, Neutral, Gear1, Gear2, Gear3, Gear4, Gear5, Gear6, Reverse, Camera, LookLeft, LookRight, Pause,
    }

    public static class DriveActions
    {
        /// <summary>Оси руля — назначаются калибровкой, а не кнопкой.</summary>
        public static bool IsAxis(DriveAction a) => a <= DriveAction.Clutch;

        public static string Title(DriveAction a)
        {
            switch (a)
            {
                case DriveAction.Gas: return "Газ";
                case DriveAction.Brake: return "Тормоз";
                case DriveAction.SteerLeft: return "Руль влево";
                case DriveAction.SteerRight: return "Руль вправо";
                case DriveAction.Clutch: return "Сцепление";
                case DriveAction.Ignition: return "Зажигание";
                case DriveAction.Starter: return "Стартер (держать)";
                case DriveAction.Handbrake: return "Стояночный тормоз";
                case DriveAction.Belt: return "Ремень безопасности";
                case DriveAction.LeftSignal: return "Левый поворотник";
                case DriveAction.RightSignal: return "Правый поворотник";
                case DriveAction.Hazard: return "Аварийная сигнализация";
                case DriveAction.Lights: return "Габариты / ближний свет";
                case DriveAction.HighBeam: return "Дальний свет";
                case DriveAction.Flash: return "Моргнуть дальним";
                case DriveAction.Horn: return "Звуковой сигнал";
                case DriveAction.Wipers: return "Дворники";
                case DriveAction.Washer: return "Омыватель";
                case DriveAction.Park: return "АКПП: P";
                case DriveAction.Neutral: return "Нейтраль";
                case DriveAction.Gear1: return "1-я передача (АКПП: D)";
                case DriveAction.Gear2: return "2-я передача";
                case DriveAction.Gear3: return "3-я передача";
                case DriveAction.Gear4: return "4-я передача";
                case DriveAction.Gear5: return "5-я передача";
                case DriveAction.Gear6: return "6-я передача";
                case DriveAction.Reverse: return "Задний ход";
                case DriveAction.Camera: return "Камера";
                case DriveAction.LookLeft: return "Посмотреть влево";
                case DriveAction.LookRight: return "Посмотреть вправо";
                case DriveAction.Pause: return "Пауза";
                default: return a.ToString();
            }
        }
    }
}
