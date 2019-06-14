﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Phantasma.Blockchain;
using Phantasma.Blockchain.Contracts;
using Phantasma.Blockchain.Contracts.Native;
using Phantasma.Blockchain.Tokens;
using Phantasma.Blockchain.Utils;
using Phantasma.Cryptography;
using Phantasma.Numerics;
using Phantasma.Storage;
using Phantasma.VM.Utils;
using static Phantasma.Blockchain.Contracts.Native.ExchangeOrderSide;

namespace Phantasma.Tests
{
    [TestClass]
    public class ExchangeTests
    {
        private static KeyPair simulatorOwner = KeyPair.Generate();
        private static ChainSimulator simulator = new ChainSimulator(simulatorOwner, 1234);

        [TestMethod]
        public void TestImmediateOrCancelLimitOrder()
        {
            var baseSymbol = Nexus.StakingTokenSymbol;
            var quoteSymbol = Nexus.FuelTokenSymbol;

            var buyer = new ExchangeUser(baseSymbol, quoteSymbol);
            var seller = new ExchangeUser(baseSymbol, quoteSymbol);

            buyer.FundQuoteToken(quantity: 2m, fundFuel: true);
            seller.FundBaseToken(quantity: 2m, fundFuel: true);

            //-----------------------------------------
            //test order amount and prices below limit
            try
            {
                buyer.OpenLimitOrder(0, 0.5m, Buy, IoC: true);
                Assert.IsTrue(false, "Order should fail due to insufficient amount");
            }
            catch (Exception e) { }
            try
            {
                buyer.OpenLimitOrder(0.5m, 0, Buy, IoC: true);
                Assert.IsTrue(false, "Order should fail due to insufficient price");
            }
            catch (Exception e) { }

            //-----------------------------------------
            //test order amount and prices at the limit

            var minimumBaseToken = UnitConversion.ToDecimal((BigInteger)simulator.Nexus.RootChain.InvokeContract("exchange", "GetMinimumSymbolQuantity", buyer.baseToken.Decimals), buyer.baseToken.Decimals);
            var minimumQuoteToken = UnitConversion.ToDecimal((BigInteger)simulator.Nexus.RootChain.InvokeContract("exchange", "GetMinimumSymbolQuantity", buyer.quoteToken.Decimals), buyer.baseToken.Decimals);

            buyer.OpenLimitOrder(minimumBaseToken, minimumQuoteToken, Buy, IoC: true);


            //-----------------------------------------
            //test unmatched IoC orders 
            Assert.IsTrue(buyer.OpenLimitOrder(0.123m, 0.3m, Buy, IoC: true) == 0, "Shouldn't have filled any part of the order");
            Assert.IsTrue(seller.OpenLimitOrder(0.123m, 0.3m, Sell, IoC: true) == 0, "Shouldn't have filled any part of the order");
            
            //-----------------------------------------
            //test partial IoC orders

            //TODO: test multiple IoC orders against each other on the same block!
        }

        [TestMethod]
        public void TestMarketBuy()
        {
            var buyer = new ExchangeUser(Nexus.StakingTokenSymbol, Nexus.FuelTokenSymbol);
            var seller = new ExchangeUser(Nexus.StakingTokenSymbol, Nexus.FuelTokenSymbol);

            //buyer.FundUser(fundBase: false, quantity: 2m, fundFuel: true);
            //seller.FundUser(fundBase: true, quantity: 2m, fundFuel: true);

            //TODO: test multiple IoC orders against each other on the same block!
        }

        #region AuxFunctions

        private void CreateToken()
        {
            var symbol = "BLA";

            var tokenSupply = UnitConversion.ToBigInteger(10000, 18);
            simulator.BeginBlock();
            simulator.GenerateToken(simulatorOwner, symbol, "BlaToken", tokenSupply, 18, TokenFlags.Transferable | TokenFlags.Fungible | TokenFlags.Finite | TokenFlags.Divisible);
            simulator.MintTokens(simulatorOwner, symbol, tokenSupply);
            simulator.EndBlock();
        }

        class ExchangeUser
        {
            private readonly KeyPair user;
            public TokenInfo baseToken;
            public TokenInfo quoteToken;

            public enum TokenType { Base, Quote}

            public ExchangeUser(string baseSymbol, string quoteSymbol)
            {
                user = KeyPair.Generate();
                baseToken = simulator.Nexus.GetTokenInfo(baseSymbol);
                quoteToken = simulator.Nexus.GetTokenInfo(quoteSymbol);
            }

            public decimal OpenLimitOrder(BigInteger orderSize, BigInteger orderPrice, ExchangeOrderSide side, bool IoC = false)
            {
                return OpenLimitOrder(UnitConversion.ToDecimal(orderSize, baseToken.Decimals), UnitConversion.ToDecimal(orderPrice, quoteToken.Decimals), side, IoC);
            }

            //Opens a limit order and returns how many tokens the user purchased/sold
            public decimal OpenLimitOrder(decimal orderSize, decimal orderPrice, ExchangeOrderSide side, bool IoC = false)
            {
                var nexus = simulator.Nexus;       

                var baseSymbol = baseToken.Symbol;
                var baseDecimals = baseToken.Decimals;
                var quoteSymbol = quoteToken.Symbol;
                var quoteDecimals = quoteToken.Decimals;

                var orderSizeBigint = UnitConversion.ToBigInteger(orderSize, baseDecimals);
                var orderPriceBigint = UnitConversion.ToBigInteger(orderPrice, quoteDecimals);

                var OpenerBaseTokensInitial = simulator.Nexus.RootChain.GetTokenBalance(baseSymbol, user.Address);
                var OpenerQuoteTokensInitial = simulator.Nexus.RootChain.GetTokenBalance(quoteSymbol, user.Address);

                BigInteger OpenerBaseTokensDelta = 0;
                BigInteger OpenerQuoteTokensDelta = 0;

                //get the starting balance for every address on the opposite side of the orderbook, so we can compare it to the final balance of each of those addresses
                var otherSide = side == Buy ? Sell : Buy;
                var startingOppositeOrderbook = (ExchangeOrder[])simulator.Nexus.RootChain.InvokeContract("exchange", "GetOrderBook", baseSymbol, quoteSymbol, otherSide);
                var OtherAddressesTokensInitial = new Dictionary<Address, BigInteger>();

                //*******************************************************************************************************************************************************************************
                //*** the following method to check token balance state only works for the scenario of a single new exchange order per block that triggers other pre-existing exchange orders ***
                //*******************************************************************************************************************************************************************************

                foreach (var oppositeOrder in startingOppositeOrderbook)
                {
                    if (OtherAddressesTokensInitial.ContainsKey(oppositeOrder.Creator) == false)
                    {
                        var targetSymbol = otherSide == Buy ? baseSymbol : quoteSymbol;
                        OtherAddressesTokensInitial.Add(oppositeOrder.Creator, simulator.Nexus.RootChain.GetTokenBalance(targetSymbol, oppositeOrder.Creator));
                    }
                }


                simulator.BeginBlock();
                var tx = simulator.GenerateCustomTransaction(user, () =>
                    ScriptUtils.BeginScript().AllowGas(user.Address, Address.Null, 1, 9999)
                        .CallContract("exchange", "OpenLimitOrder", user.Address, baseSymbol, quoteSymbol, orderSizeBigint, orderPriceBigint, side, IoC).
                        SpendGas(user.Address).EndScript());
                simulator.EndBlock();

                var txCost = simulator.Nexus.RootChain.GetTransactionFee(tx);

                BigInteger escrowedAmount = 0;

                //take into account the transfer of the owner's wallet to the chain address
                if (side == Buy)
                {
                    escrowedAmount = UnitConversion.ToBigInteger(orderSize * orderPrice, quoteDecimals);
                    OpenerQuoteTokensDelta -= escrowedAmount;
                }
                else if (side == Sell)
                {
                    escrowedAmount = orderSizeBigint;
                    OpenerBaseTokensDelta -= escrowedAmount;
                }

                //take into account tx cost in case one of the symbols is the FuelToken
                if (baseSymbol == Nexus.FuelTokenSymbol)
                {
                    OpenerBaseTokensDelta -= txCost;
                }
                else
                if (quoteSymbol == Nexus.FuelTokenSymbol)
                {
                    OpenerQuoteTokensDelta -= txCost;
                }

                var events = nexus.FindBlockByTransaction(tx).GetEventsForTransaction(tx.Hash);

                var wasNewOrderCreated = events.Count(x => x.Kind == EventKind.OrderCreated && x.Address == user.Address) == 1;
                Assert.IsTrue(wasNewOrderCreated, "Order was not created");

                var wasNewOrderClosed = events.Count(x => x.Kind == EventKind.OrderClosed && x.Address == user.Address) == 1;
                var wasNewOrderCancelled = events.Count(x => x.Kind == EventKind.OrderCancelled && x.Address == user.Address) == 1;

                var createdOrderEvent = events.First(x => x.Kind == EventKind.OrderCreated);
                var createdOrderUid = Serialization.Unserialize<BigInteger>(createdOrderEvent.Data);
                ExchangeOrder createdOrderPostFill = new ExchangeOrder();

                //----------------
                //verify the order is still in the orderbook according to each case

                //in case the new order was IoC and it wasnt closed, order should have been cancelled
                if (wasNewOrderClosed == false && IoC)
                {
                    Assert.IsTrue(wasNewOrderCancelled, "Non closed IoC order did not get cancelled");
                }
                else
                //if the new order was closed
                if (wasNewOrderClosed)
                {
                    //and check that the order no longer exists on the orderbook
                    try
                    {
                        simulator.Nexus.RootChain.InvokeContract("exchange", "GetExchangeOrder", createdOrderUid);
                        Assert.IsTrue(false, "Closed order exists on the orderbooks");
                    }
                    catch (Exception e)
                    {
                        //purposefully empty, this is the expected code-path
                    }
                }
                else //if the order was not IoC and it wasn't closed, then:
                {
                    Assert.IsTrue(IoC == false, "All IoC orders should have been triggered by the previous ifs");

                    //check that it still exists on the orderbook
                    try
                    {
                        createdOrderPostFill = (ExchangeOrder)simulator.Nexus.RootChain.InvokeContract("exchange", "GetExchangeOrder", createdOrderUid);
                    }
                    catch (Exception e)
                    {
                        Assert.IsTrue(false, "Non-IoC unclosed order does not exist on the orderbooks");
                    }
                }
                //------------------

                //------------------
                //validate that everyone received their tokens appropriately

                BigInteger escrowedUsage = 0;   //this will hold the amount of the escrowed amount that was actually used in the filling of the order
                                                //for IoC orders, we need to make sure that what wasn't used gets returned properly
                                                //for non IoC orders, we need to make sure that what wasn't used stays on the orderbook
                BigInteger baseTokensReceived = 0, quoteTokensReceived = 0;
                var OtherAddressesTokensDelta = new Dictionary<Address, BigInteger>();

                //*******************************************************************************************************************************************************************************
                //*** the following method to check token balance state only works for the scenario of a single new exchange order per block that triggers other pre-existing exchange orders ***
                //*******************************************************************************************************************************************************************************

                //calculate the expected delta of the balances of all addresses involved
                var tokenExchangeEvents = events.Where(x => x.Kind == EventKind.TokenReceive);

                foreach (var tokenExchangeEvent in tokenExchangeEvents)
                {
                    var eventData = Serialization.Unserialize<TokenEventData>(tokenExchangeEvent.Data);

                    if (tokenExchangeEvent.Address == user.Address)
                    {
                        if(eventData.symbol == baseSymbol)
                            baseTokensReceived += eventData.value;
                        else
                        if(eventData.symbol == quoteSymbol)
                            quoteTokensReceived += eventData.value;
                    }
                    else
                    {
                        Assert.IsTrue(OtherAddressesTokensInitial.ContainsKey(tokenExchangeEvent.Address), "Address that was not on this orderbook received tokens");
                        Assert.IsTrue(OtherAddressesTokensDelta.ContainsKey(tokenExchangeEvent.Address) == false, "Order opener tried to fill the same order more than once, should not be possible");

                        OtherAddressesTokensDelta.Add(tokenExchangeEvent.Address, eventData.value);
                        escrowedUsage += eventData.value;   //the tokens other addresses receive come from the escrowed amount of the order opener
                    }
                }

                OpenerBaseTokensDelta += baseTokensReceived;
                OpenerQuoteTokensDelta += quoteTokensReceived;

                var leftOver = escrowedAmount - escrowedUsage;

                if (wasNewOrderClosed)
                {
                    Assert.IsTrue(leftOver == 0);
                }
                else if (IoC)
                {
                    switch (side)
                    {
                        case Buy:
                            Assert.IsTrue(OpenerQuoteTokensDelta == escrowedUsage - (quoteSymbol == Nexus.FuelTokenSymbol ? txCost : 0));
                            break;

                        case Sell:
                            Assert.IsTrue(OpenerBaseTokensDelta == escrowedUsage - (baseSymbol == Nexus.FuelTokenSymbol ? txCost : 0));
                            break;
                    }
                }
                else //if the user order was not closed and it wasnt IoC, it should have the correct unfilled amount
                {
                    Assert.IsTrue(leftOver == createdOrderPostFill.Amount);
                }


                //get the actual final balance of all addresses involved and make sure it matches the expected deltas
                var OpenerBaseTokensFinal = simulator.Nexus.RootChain.GetTokenBalance(baseSymbol, user.Address);
                var OpenerQuoteTokensFinal = simulator.Nexus.RootChain.GetTokenBalance(quoteSymbol, user.Address);

                Assert.IsTrue(OpenerBaseTokensFinal == OpenerBaseTokensDelta + OpenerBaseTokensInitial);
                Assert.IsTrue(OpenerQuoteTokensFinal == OpenerQuoteTokensDelta + OpenerQuoteTokensInitial);

                foreach (var entry in OtherAddressesTokensInitial)
                {
                    var otherAddressInitialTokens = entry.Value;
                    BigInteger delta = 0;

                    if (OtherAddressesTokensDelta.ContainsKey(entry.Key))
                        delta = OtherAddressesTokensDelta[entry.Key];

                    var targetSymbol = otherSide == Buy ? baseSymbol : quoteSymbol;

                    var otherAddressFinalTokens = simulator.Nexus.RootChain.GetTokenBalance(targetSymbol, entry.Key);

                    Assert.IsTrue(otherAddressFinalTokens == delta + otherAddressInitialTokens);
                }

                return side == Buy ? UnitConversion.ToDecimal(baseTokensReceived, baseToken.Decimals) : UnitConversion.ToDecimal(quoteTokensReceived, quoteToken.Decimals);
            }

            public void FundBaseToken(decimal quantity, bool fundFuel = false) => FundUser(true, quantity, fundFuel);
            public void FundQuoteToken(decimal quantity, bool fundFuel = false) => FundUser(false, quantity, fundFuel);


            //transfers the given quantity of a specified token to this user, plus some fuel to pay for transactions
            private void FundUser(bool fundBase, decimal quantity, bool fundFuel = false)
            {
                var nexus = simulator.Nexus;
                var token = fundBase ? baseToken : quoteToken;

                simulator.BeginBlock();
                simulator.GenerateTransfer(simulatorOwner, user.Address, nexus.RootChain, token.Symbol, UnitConversion.ToBigInteger(quantity, token.Decimals));

                if (fundFuel)
                    simulator.GenerateTransfer(simulatorOwner, user.Address, nexus.RootChain, Nexus.FuelTokenSymbol, 500000);

                simulator.EndBlock();
            }
        }

        

        #endregion
    }
}
