"""Модули-системы мира. Каждый файл — отдельная подключаемая механика."""
from .needs import NeedsSystem
from .work import WorkSystem
from .tavern import TavernSystem
from .consumption import ConsumptionSystem
from .events import EventsSystem
from .population import PopulationSystem
from .weather import WeatherSystem
from .quests import QuestSystem
from .season import SeasonSystem
from .alife import AlifeSystem

__all__ = ["NeedsSystem", "WorkSystem", "TavernSystem", "ConsumptionSystem",
           "EventsSystem", "PopulationSystem", "WeatherSystem", "QuestSystem",
           "SeasonSystem", "AlifeSystem"]
