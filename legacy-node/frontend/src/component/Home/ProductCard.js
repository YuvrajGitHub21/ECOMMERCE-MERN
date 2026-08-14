/* eslint-disable */

import React from "react";
import { Link } from "react-router-dom";
import Rating from '@material-ui/lab/Rating';

 const ProductCard = ({ product }) => {
  const options = {
    value: product.ratings,
    readOnly: true,
    precision: 0.5,
    isHalf : true,
  };

  const imageUrl = product.images && product.images[0] ? product.images[0].url : "https://dcassetcdn.com/design_img/3927828/616713/26120406/54pa1kvna4ypc21bez1tn35g8g_image.jpg"; // Replace with your default image URL

  return (
    <Link className="productCard" to={`/product/${product._id}`}>
      <img src={imageUrl} alt={product.name} />
      <p>{product.name}</p>
      <div>
        <Rating {...options} />{" "}
        <span className="productCardSpan">
          {" "}
          ({product.numOfReviews} Reviews)
        </span>
      </div>
      <span>{`₹${product.price}`}</span>
    </Link>

  );
};

export default ProductCard;