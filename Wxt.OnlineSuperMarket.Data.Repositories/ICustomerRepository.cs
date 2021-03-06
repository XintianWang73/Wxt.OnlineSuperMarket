﻿namespace Wxt.OnlineSuperMarket.Data.Repositories
{
    using Wxt.OnlineSuperMarket.Data.Entities;

    public interface ICustomerRepository
    {
#if DEBUG
        void ReinitializeRepository();
#endif
        Customer AddCustomer(Customer customer);

        int Login(string userName, string password);

        void DeleteCustomer(int customerId);

        void AddToCart(int customerId, int productId, int count);

        int RemoveFromCart(int customerId, int productId, int count);

        string ListShoppingCart(int customerId);

        void ClearCart(int customerId);

        Receipt CheckOut(int customerId);
    }
}
