import React, { Fragment, useEffect, useRef, useState } from "react";
import CheckoutSteps from "../Cart/CheckoutSteps";
import { useSelector, useDispatch } from "react-redux";
import MetaData from "../layout/MetaData";
import { Typography } from "@material-ui/core";
import { useAlert } from "react-alert";
import CreditCardIcon from "@material-ui/icons/CreditCard";
import EventIcon from "@material-ui/icons/Event";
import VpnKeyIcon from "@material-ui/icons/VpnKey";
import "react-datepicker/dist/react-datepicker.css";

import "./payment.css";
import { createOrder, clearErrors } from "../../actions/orderAction";
import { useNavigate } from "react-router-dom";

const Payment = () => {
  const orderInfo = JSON.parse(sessionStorage.getItem("orderInfo"));

  const dispatch = useDispatch();
  const alert = useAlert();
  const navigate = useNavigate();
  const payBtn = useRef(null);

  const { shippingInfo, cartItems } = useSelector((state) => state.cart);
  //const { user } = useSelector((state) => state.user);
  const { error } = useSelector((state) => state.newOrder);

  const [cardNumber, setCardNumber] = useState("");
  const [expiryDate, setExpiryDate] = useState("");
  const [cvc, setCvc] = useState("");

  const order = {
    shippingInfo,
    orderItems: cartItems,
    itemsPrice: orderInfo.subtotal,
    taxPrice: orderInfo.tax,
    shippingPrice: orderInfo.shippingCharges,
    totalPrice: orderInfo.totalPrice,
  };

  const validateCardNumber = (number) => {
    const regex = /^\d{16}$/;
    return regex.test(number);
  };

  const validateExpiryDate = (date) => {
    const regex = /^(0[1-9]|1[0-2])\/?([0-9]{2})$/;
    return regex.test(date);
  };

  const validateCvc = (cvc) => {
    const regex = /^\d{3}$/;
    return regex.test(cvc);
  };

  const submitHandler = async (e) => {
    e.preventDefault();
    payBtn.current.disabled = true;

    if (!validateCardNumber(cardNumber)) {
      alert.error("Invalid Card Number");
      payBtn.current.disabled = false;
      return;
    }

    if (!validateExpiryDate(expiryDate)) {
      alert.error("Invalid Expiry Date");
      payBtn.current.disabled = false;
      return;
    }

    if (!validateCvc(cvc)) {
      alert.error("Invalid CVC");
      payBtn.current.disabled = false;
      return;
    }

    // Mock payment processing
    setTimeout(() => {
      order.paymentInfo = {
        id: "mock_payment_id",
        status: "succeeded",
      };

      dispatch(createOrder(order));
      navigate("/success");
    }, 2000);
  };

  useEffect(() => {
    if (error) {
      alert.error(error);
      dispatch(clearErrors());
    }
  }, [dispatch, error, alert]);

  return (
    <Fragment>
      <MetaData title="Payment" />
      <CheckoutSteps activeStep={2} />
      <div className="paymentContainer">
        <form className="paymentForm" onSubmit={submitHandler}>
          <Typography>Card Info</Typography>
          <div className="paymentInputContainer">
            <div className="paymentInputField">
              <CreditCardIcon />
              <input
                type="text"
                placeholder="Card Number"
                className="paymentInput"
                value={cardNumber}
                onChange={(e) => setCardNumber(e.target.value)}
                required
              />
            </div>
            <div className="paymentInputField">
              <EventIcon />
              <input
                type="text"
                placeholder="Expiry Date (MM/YY)"
                className="paymentInput"
                value={expiryDate}
                onChange={(e) => setExpiryDate(e.target.value)}
                required
              />
            </div>
            <div className="paymentInputField">
              <VpnKeyIcon />
              <input
                type="text"
                placeholder="CVC"
                className="paymentInput"
                value={cvc}
                onChange={(e) => setCvc(e.target.value)}
                required
              />
            </div>
          </div>
          <input
            type="submit"
            value={`Pay - ₹${orderInfo && orderInfo.totalPrice}`}
            ref={payBtn}
            className="paymentFormBtn"
            style={{ backgroundColor: "orange" }}
          />
        </form>
      </div>
    </Fragment>
  );
};

export default Payment;
