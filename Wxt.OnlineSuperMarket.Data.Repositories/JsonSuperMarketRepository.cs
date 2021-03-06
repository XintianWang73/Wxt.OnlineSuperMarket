﻿namespace Wxt.OnlineSuperMarket.Data.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Wxt.OnlineSuperMarket.Data.Entities;

    public class JsonSuperMarketRepository : ISuperMarketRepository
    {
        private const string _productFile = "products.json";

        private const string _stockFile = "stock.json";

        private const string _receiptFile = "receipts.json";

        private const string _productAndStockLockerFile = "productandstock.lk";

        private const string _receiptLockerFile = "receipts.lk";

        private readonly JsonHandler _jsonHandler = new JsonHandler();

        private readonly ShareFileLocker _FileLocker = new ShareFileLocker();

        private ProductRecords Products { get; set; }

        private List<ProductItem> Stocks { get; set; }

        private ReceiptRecords Receipts { get; set; }

        class ProductRecords
        {
            public int ProductCurrentId { get; set; }

            public List<Product> ProductList { get; set; }
        }

        class ReceiptRecords
        {
            public int ReceiptCurrentId { get; set; }

            public List<Receipt> ReceiptList { get; set; }
        }

#if DEBUG
        public void ReinitializeRepository()
#else
        private void ReinitializeRepository()
#endif
        {
            Products = new ProductRecords
            {
                ProductList = new List<Product>
                {
                    new Product { Id = 1, Name = "banana", Category = Category.Grocery, Description = "Banana from Mexico", Price = 1.67m },
                    new Product { Id = 2, Name = "apple", Category = Category.Grocery, Description = "Apple from China", Price = 2.67m },
                    new Product { Id = 3, Name = "Television", Category = Category.Electronic, Description = "Sony 65\"", Price = 1600.59m }
                },
                ProductCurrentId = 3
            };

            Stocks = new List<ProductItem>
            {
                new ProductItem { ProductId = 1, Count = 100 },
                new ProductItem { ProductId = 2, Count = 200 },
                new ProductItem { ProductId = 3, Count = 300 },
            };

            Receipts = new ReceiptRecords()
            {
                ReceiptCurrentId = 0,
                ReceiptList = new List<Receipt>()
            };

            _jsonHandler.SaveRecords(_productFile, Products);

            _jsonHandler.SaveRecords(_stockFile, Stocks);

            _jsonHandler.SaveRecords(_receiptFile, Receipts);
        }

        private void GetProducts()
        {
            Products = _jsonHandler.GetRecords<ProductRecords>(_productFile);
            if (Products == null)
            {
                ReinitializeRepository();
            }
        }

        private void GetStocks()
        {
            Stocks = _jsonHandler.GetRecords<List<ProductItem>>(_stockFile);
            if (Stocks == null)
            {
                ReinitializeRepository();
            }
        }

        private void GetReceipts()
        {
            Receipts = _jsonHandler.GetRecords<ReceiptRecords>(_receiptFile);
            if (Receipts == null)
            {
                Receipts = new ReceiptRecords()
                {
                    ReceiptCurrentId = 0,
                    ReceiptList = new List<Receipt>()
                };

                _jsonHandler.SaveRecords(_receiptFile, Receipts);
            }
        }

        public Product AddProduct(Product product)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.Name) || product.Price < 0m)
            {
                throw new ArgumentNullException("Cannot create new product.");
            }

            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            GetProducts();

            product.Id = ++Products.ProductCurrentId;

            if (string.IsNullOrWhiteSpace(product.Description))
            {
                product.Description = product.Name;
            }

            Products.ProductList.Add(product);

            try
            {
                _jsonHandler.SaveRecords(_productFile, Products);
                return product;
            }
            finally
            {
                _FileLocker.UnlockObj(locker);
            }
        }

        private Product FindProductInner(int id)
        {
            GetProducts();
            return Products.ProductList.FirstOrDefault(p => p.Id == id);
        }

        public Product FindProduct(int id)
        {
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);
            var product = FindProductInner(id);
            _FileLocker.UnlockObj(locker);
            return product;
        }

        public void RemoveProduct(int productId)
        {
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            var product = FindProductInner(productId);

            try
            {
                if (product != null)
                {
                    GetStocks();

                    var stock = Stocks.FirstOrDefault(s => s.ProductId == productId);

                    if (stock != null)
                    {
                        throw new InvalidOperationException("Cannot remove product which is still in stock.");
                    }
                    if (!Products.ProductList.Remove(product))
                    {
                        throw new InvalidOperationException("Unknow problem.");
                    }
                }
                else
                {
                    throw new IndexOutOfRangeException($"Product {productId} does not exist.");
                }

                _jsonHandler.SaveRecords(_productFile, Products);
            }
            finally
            {
                _FileLocker.UnlockObj(locker);
            }
        }

        public Receipt Checkout(List<ProductItem> items)
        {
            if (items == null || items.Count == 0)
            {
                throw new InvalidOperationException("There is nothing in the shopping cart.");
            }

            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);
            FileStream lockerReceipt = null;

            try
            {
                GetProducts();
                GetStocks();

                if (items.Exists(
                    i => Products.ProductList.FirstOrDefault(
                        p => p.Id == i.ProductId) == null))
                {
                    throw new InvalidOperationException($"Some products do not exist.");
                }

                if (items.Exists(
                    i => Stocks.FirstOrDefault(
                        s => s.ProductId == i.ProductId && s.Count >= i.Count) == null))
                {
                    throw new InvalidOperationException($"Some products are out of stock or not enough.");
                }

                Receipt receipt = new Receipt()
                {
                    ShoppingItems = new List<ShoppingItem>(),
                    TransactionTime = DateTimeOffset.Now
                };

                foreach (var item in items)
                {
                    int productId = item.ProductId;
                    int count = item.Count;
                    var stock = Stocks.FirstOrDefault(s => s.ProductId == productId && s.Count >= count);
                    stock.Count -= count;
                    if (stock.Count == 0)
                    {
                        Stocks.Remove(stock);
                    }

                    Product product = FindProductInner(productId);
                    ShoppingItem shoppingItem = new ShoppingItem()
                    {
                        ProductId = item.ProductId,
                        ProductName = product.Name,
                        Price = product.Price,
                        Count = count
                    };
                    receipt.ShoppingItems.Add(shoppingItem);
                }

                lockerReceipt = _FileLocker.LockObj(_receiptLockerFile);

                GetReceipts();
                receipt.Id = ++Receipts.ReceiptCurrentId;
                Receipts.ReceiptList.Add(receipt);

                _jsonHandler.SaveRecords(_receiptFile, Receipts);
                _jsonHandler.SaveRecords(_stockFile, Stocks);

                return receipt;
            }
            finally
            {
                _FileLocker.UnlockObj(locker);
                if (lockerReceipt != null)
                {
                    _FileLocker.UnlockObj(lockerReceipt);
                }
            }
        }

        public void DecreaseStock(int productId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException($"Cannot decrease {count} product from stock.");
            }

            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            var product = FindProductInner(productId);
            try
            {
                if (product != null)
                {
                    GetStocks();

                    var stock = Stocks.FirstOrDefault(s => s.ProductId == productId && s.Count >= count);

                    if (stock != null)
                    {
                        stock.Count -= count;
                        if (stock.Count == 0)
                        {
                            Stocks.Remove(stock);
                        }
                        _jsonHandler.SaveRecords(_stockFile, Stocks);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Product {productId} is out of stock or not enough.");
                    }
                }
                else
                {
                    throw new IndexOutOfRangeException($"Product {productId} does not exist.");
                }
            }
            finally
            {
                _FileLocker.UnlockObj(locker);
            }
        }

        public int GetStock(int productId)
        {
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            GetStocks();

            var stock = Stocks.FirstOrDefault(s => s.ProductId == productId);

            _FileLocker.UnlockObj(locker);

            return stock?.Count ?? -1;
        }

        public void IncreaseStock(int productId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException($"Cannot increase {count} product to stock.");
            }
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            var product = FindProductInner(productId);

            try
            {
                if (product != null)
                {
                    GetStocks();

                    var stock = Stocks.FirstOrDefault(s => s.ProductId == productId);

                    if (stock != null)
                    {
                        stock.Count += count;
                    }
                    else
                    {
                        Stocks.Add(new ProductItem() { ProductId = productId, Count = count });
                    }

                    _jsonHandler.SaveRecords(_stockFile, Stocks);
                }
                else
                {
                    throw new IndexOutOfRangeException($"Product {productId} does not exist.");
                }
            }
            finally
            {
                _FileLocker.UnlockObj(locker);
            }
        }

        public string ListProducts()
        {
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            GetProducts();
            GetStocks();

            List<string> results = new List<string>();
            foreach (Product p in Products.ProductList)
            {
                var stock = Stocks.FirstOrDefault(s => s.ProductId == p.Id);
                results.Add(p.ToString() + $" Stock = {stock?.Count ?? 0}");
            }

            _FileLocker.UnlockObj(locker);

            return results.Count > 0
                ? string.Join(Environment.NewLine, results)
                : "";
        }

        public string ListStocks()
        {
            FileStream locker = _FileLocker.LockObj(_productAndStockLockerFile);

            GetProducts();
            GetStocks();

            List<string> results = new List<string>();
            foreach (ProductItem s in Stocks)
            {
                var product = Products.ProductList.FirstOrDefault(p => p.Id == s.ProductId);
                results.Add($"{product?.ToString() ?? "Product Info : Unknown"} Stock = {s.Count}");
            }

            _FileLocker.UnlockObj(locker);

            return results.Count > 0
                ? string.Join(Environment.NewLine, results)
                : "";
        }

        public string ListReceipts()
        {
            FileStream locker = _FileLocker.LockObj(_receiptLockerFile);

            GetReceipts();

            List<string> results = new List<string>();
            foreach (Receipt r in Receipts.ReceiptList)
            {
                results.Add($"{r.ToString()}");
            }

            _FileLocker.UnlockObj(locker);

            return results.Count > 0
                ? string.Join(Environment.NewLine, results)
                : "";
        }
    }
}
