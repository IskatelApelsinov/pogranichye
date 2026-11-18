from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
GAMEPLAY = ROOT / "Assets/_Project/Scripts/Runtime/CoreGameplay.cs"


class ShopCatalogueSourceChecks(unittest.TestCase):
    def test_shop_uses_only_server_catalogue(self):
        source = GAMEPLAY.read_text(encoding="utf-8")
        self.assertNotIn("TakePrice", source)
        self.assertNotIn("TakeAt", source)
        self.assertIn('Arr(_catalog, "items")', source)
        self.assertIn('Num(definition, "price")', source)
        self.assertIn('Str(definition, "at")', source)
        self.assertIn("we do not invent a local shop", source)

    def test_shop_does_not_offer_hardcoded_items_without_catalogue(self):
        source = GAMEPLAY.read_text(encoding="utf-8")
        marker = 'var catalogItemsForSale = Arr(_catalog, "items");'
        start = source.index(marker)
        end = source.index('var recipes = Arr(_me, "recipes");', start)
        shop_block = source[start:end]
        self.assertIn("Ассортимент пока неизвестен", shop_block)
        self.assertNotIn("foreach (var pair in", shop_block)


if __name__ == "__main__":
    unittest.main()
